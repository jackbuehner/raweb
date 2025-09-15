using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RAWebInstaller
{
  partial class OSHelpers
  {
    /// <summary>
    /// Detects if OS is a Windows Server edition.
    /// </summary>
    public static bool IsServer()
    {
      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      string productName = key?.GetValue("ProductName")?.ToString() ?? "";
      return productName.Contains("Server", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects if OS is a Windows Home edition.
    /// </summary>
    /// <returns></returns>
    public static bool IsHome()
    {
      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      string productName = key?.GetValue("ProductName")?.ToString() ?? "";
      return productName.Contains("Home", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the OS is supported by RAWeb.
    /// </summary>
    /// <returns></returns>
    public static bool IsSupportedOS()
    {
      int requiredServerBuild = 14393; // Server 2016
      int requiredClientBuild = 10240; // Windows 10 1507
      int requiredClientHomeBuild = 18362; // Windows 10 19H1

      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      string buildStr = key?.GetValue("CurrentBuildNumber")?.ToString() ?? ""; // major build number
      string ubrStr = key?.GetValue("UBR")?.ToString() ?? "0"; // minor build number

      int build = int.TryParse(buildStr, out var b) ? b : 0;
      int ubr = int.TryParse(ubrStr, out var u) ? u : 0;

      if (IsServer())
        return build > requiredServerBuild || (build == requiredServerBuild && ubr >= 0);
      else if (IsHome())
        return build > requiredClientHomeBuild || (build == requiredClientHomeBuild && ubr >= 0);
      else
        return build > requiredClientBuild || (build == requiredClientBuild && ubr >= 0);
    }

    /// <summary>
    /// Creats a temporary folder with an optional prefix.
    /// </summary>
    /// <param name="prefix"></param>
    /// <returns></returns>
    public static string GetTempFolder(string? prefix = null)
    {
      string tempFolder = Path.GetTempPath();
      string tempDirName = (prefix ?? "raweb_") + Guid.NewGuid().ToString("N");
      string fullPath = Path.Combine(tempFolder, tempDirName);

      Directory.CreateDirectory(fullPath);
      return fullPath;
    }

    /// <summary>
    /// Takes ownership of a file or directory for the specified user account,
    /// using the Windows `takeown.exe` command.
    /// </summary>
    /// <param name="path">File or directory path</param>
    /// <param name="account">Optional Windows account (defaults to BUILTIN\Administrators).</param>
    public static void TakeOwnership(string path, NTAccount? account = null)
    {
      if (!File.Exists(path) && !Directory.Exists(path))
        throw new FileNotFoundException($"Path not found: {path}");

      account ??= new NTAccount("BUILTIN", "Administrators");
      string accountName = account.Value;

      // `/r` = recursive, `/d y` = assume Yes on prompts
      // `/a` = give ownership to Administrators group
      // `/user <name>` = give ownership to specific user

      string takeownArgs;
      if (accountName.Equals("BUILTIN\\Administrators", StringComparison.OrdinalIgnoreCase) ||
          accountName.Equals("Administrators", StringComparison.OrdinalIgnoreCase))
      {
        takeownArgs = $"/f \"{path}\" /r /d y /a";
      }
      else
      {
        takeownArgs = $"/f \"{path}\" /r /d y /user \"{accountName}\"";
      }

      CommandRunner.Run("takeown", takeownArgs);
    }

    /// <summary>
    /// Takes ownership of a file or directory and grants full control
    /// permissions to the specified account (defaults to Administrators).
    /// </summary>
    /// <param name="path">The file or directory path</param>
    /// <param name="account">Optional Windows account (defaults to BUILTIN\Administrators).</param>
    public static void TakeControl(string path, NTAccount? account = null)
    {
      account ??= new NTAccount("BUILTIN", "Administrators");

      TakeOwnership(path, account);

      // grant full control recursively using icacls
      string accountName = account.Value;
      string icaclsArgs = $"\"{path}\" /grant \"{accountName}\":F /t /c";
      CommandRunner.Run("icacls", icaclsArgs, allowedExitCodes: [0]);
    }

    /// <summary>
    /// Converts a string into a URL-friendly (or Windows-path-friendly) "slug".
    /// </summary>
    /// <param name="input"></param>
    /// <param name="replacement"></param>
    /// <returns></returns>
    public static string Slugify(string input, char replacement = '-')
    {
      if (string.IsNullOrWhiteSpace(input))
        return string.Empty;

      // normalize (decompose accents, etc.)
      string normalized = input.Normalize(NormalizationForm.FormD);

      // strip non-spacing marks (accents)
      var sb = new StringBuilder();
      foreach (var ch in normalized)
      {
        var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (uc != UnicodeCategory.NonSpacingMark)
        {
          sb.Append(ch);
        }
      }

      string noAccents = sb.ToString().Normalize(NormalizationForm.FormC);

      // convert to lowercase
      string lower = noAccents.ToLowerInvariant();

      // replace non-alphanumeric with hyphens
      string hyphenated = SlugifyRegex().Replace(lower, $"{replacement}");

      // trim leading/trailing replacement characters
      string slug = hyphenated.Trim(replacement);

      return slug;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugifyRegex();

    /// <summary>
    /// Retrieves the name of the parent process for the current process.
    /// </summary>
    /// <returns>
    /// The name of the parent process if it can be determined; 
    /// otherwise, an empty string.
    /// </returns>
    public static string GetParentProcessName()
    {
      using var currentProcess = Process.GetCurrentProcess();

      try
      {
        int parentProcessId = 0;

        // Query WMI for the parent process of the current process.
        using (var searcher = new ManagementObjectSearcher(
            $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={currentProcess.Id}"))
        using (var results = searcher.Get())
        {
          foreach (ManagementObject obj in results.Cast<ManagementObject>())
          {
            parentProcessId = Convert.ToInt32(obj["ParentProcessId"]);
          }
        }

        // if we successfully retrieved the parent process ID, get its name.
        if (parentProcessId > 0)
        {
          using var parentProcess = Process.GetProcessById(parentProcessId);
          return parentProcess.ProcessName;
        }
      }
      catch { }

      // returning an empty string indicates failure.
      return string.Empty;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);


    /// <summary>
    /// Hides the console window of the current process, if it has one.
    /// </summary>
    public static void HideConsoleWindow()
    {
      // get the handle to the console window for this console app
      var consoleHandle = GetConsoleWindow();

      // hide the console window
      if (consoleHandle != IntPtr.Zero)
      {
        ShowWindow(consoleHandle, 0); // SW_HIDE = 0
      }
    }


    /// <summary>
    /// Shows the console window of the current process, if it has one.
    /// </summary>
    public static void ShowConsoleWindow()
    {
      // get the handle to the console window for this console app
      var consoleHandle = GetConsoleWindow();

      // hide the console window
      if (consoleHandle != IntPtr.Zero)
      {
        ShowWindow(consoleHandle, 1); // SW_SHOWNORMAL = 1
      }
    }
  }
}
