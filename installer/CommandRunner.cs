using System.Diagnostics;
using System.Text;
using Spectre.Console;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace RAWebInstaller
{
  public static class CommandRunner
  {
    /// <summary>
    /// Executes a command line process and captures its output.
    /// <br /><br />
    /// If running a powershell command, consider using RunPS() instead.
    /// <br /><br />
    /// If running DISM, consider using RunDism() instead.
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="arguments"></param>
    /// <param name="writeStdout"></param>
    /// <param name="writeStderr"></param>
    /// <param name="firstExitCode"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static string Run(string fileName, string arguments, bool writeStdout = false, bool writeStderr = true, int firstExitCode = 1)
    {
      var startInfo = new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = false
      };

      using var process = new Process { StartInfo = startInfo };

      var buffer = new StringBuilder();

      // capture stdout
      process.OutputDataReceived += (s, e) =>
      {
        if (!string.IsNullOrEmpty(e.Data))
        {
          buffer.AppendLine(e.Data);
          if (writeStdout) AnsiConsole.WriteLine(e.Data); // normal output
        }
      };

      // capture stderr
      process.ErrorDataReceived += (s, e) =>
      {
        if (!string.IsNullOrEmpty(e.Data))
        {
          buffer.AppendLine(e.Data);
          if (writeStderr) AnsiConsole.WriteLine($"[red]{e.Data}[/]"); // error output
        }
      };


      process.Start();
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();
      process.WaitForExit();

      var output = buffer.ToString().Trim();

      if (process.ExitCode < 0 || process.ExitCode > firstExitCode)
      {
        // robocopy treats codes < 8 as success or success with notes/error (e.g. some files skipped)
        throw new InvalidOperationException(
            $"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.{Environment.NewLine}{output}"
        );
      }

      return output;
    }

    /// <summary>
    /// Executes a PowerShell command.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="progressContext"></param>
    /// <param name="renameActivity">Optional function to rename the activity name shown in the progress context.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static string RunPS(string command, ProgressContext? progressContext = null, Func<string, string>? renameActivity = null)
    {
      // create an embedded PowerShell instance
      using var ps = PowerShell.Create();

      var buffer = new StringBuilder();

      // intercept progressbar updates and update the status context (if provided)
      if (progressContext != null)
      {
        // track created tasks - each task corresponds to a unique activity name
        Dictionary<string, ProgressTask> tasks = [];

        ps.Streams.Progress.DataAdded += (sender, @event) =>
        {
          if (sender is PSDataCollection<ProgressRecord> collection)
          {
            var psProgressRecord = collection[@event.Index];

            // if there is a callback to rename the activity, use it
            var activityName = renameActivity?.Invoke(psProgressRecord.Activity) ?? psProgressRecord.Activity;

            // create a task for each activity if it doesn't already exist
            var task = tasks.GetValueOrDefault(psProgressRecord.Activity);
            if (task == null)
            {
              task = progressContext.AddTask(activityName, maxValue: 100);
              tasks[psProgressRecord.Activity] = task;
            }

            task.Value = psProgressRecord.PercentComplete;

            if (psProgressRecord.PercentComplete >= 100)
            {
              task.StopTask();
            }
          }
        };
      }

      // force permissive execution policy
      ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force").Invoke();
      ps.Commands.Clear();

      // run the command
      ps.AddScript(command);
      var results = ps.Invoke();

      // capture output (like stdout)
      foreach (var result in results)
      {
        buffer.AppendLine(result.ToString());
      }

      // check for errors, and cif any exist, concatenate all errors and throw
      if (ps.HadErrors)
      {
        var err = string.Join(Environment.NewLine, ps.Streams.Error);
        if (!string.IsNullOrEmpty(err))
        {
          throw new InvalidOperationException(err);
        }
      }

      return buffer.ToString();
    }


    /// <summary>
    /// Executes a DISM command and optionally updates a progress task with progress information.
    /// </summary>
    /// <param name="arguments"></param>
    /// <param name="task"></param>
    /// <param name="writeStdout"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public static void RunDism(string arguments, ProgressTask? task, bool writeStdout = false)
    {
      var psi = new ProcessStartInfo
      {
        FileName = "dism.exe",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = new Process { StartInfo = psi };
      process.Start();

      // read output line by line
      while (!process.StandardOutput.EndOfStream)
      {
        var line = process.StandardOutput.ReadLine();
        if (!string.IsNullOrWhiteSpace(line))
        {
          if (writeStdout) AnsiConsole.WriteLine(line);

          if (task == null)
            continue;

          // look for progress percentage in the output line
          // and update the task in the progress bar
          var m = Regex.Match(line, @"(\d+(\.\d+)?)%");
          if (m.Success && double.TryParse(m.Groups[1].Value, out var val))
          {
            task.Value = val;
          }
        }
      }

      process.WaitForExit();
      task?.StopTask();

      if (process.ExitCode != 0)
      {
        throw new InvalidOperationException($"DISM failed with exit code {process.ExitCode}");
      }
    }
  }
}
