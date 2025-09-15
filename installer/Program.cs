using Spectre.Console.Cli;
using RAWebInstaller;
using System.Windows;
using Spectre.Console;

internal class Program
{


  [STAThread]
  public static int Main(string[] args)
  {


    // when launched from the following processes without any arguments,
    // we should show a GUI window instead of console mode
    string[] guiParents = ["explorer"];
    bool hasArgs = args.Length > 0;
    string parentName = OSHelpers.GetParentProcessName();
    bool shouldLaunchGui = !hasArgs && guiParents.Contains(parentName, StringComparer.OrdinalIgnoreCase);

    if (shouldLaunchGui)
    {
      // hide the console window
      OSHelpers.HideConsoleWindow();

      try
      {
        var app = new Application();
        var wnd = new MainWindow();
        app.Run(wnd);
        return 0;
      }
      catch (Exception ex)
      {
        // if the gui fails to launch, fall back to console mode
        OSHelpers.ShowConsoleWindow();
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
        AnsiConsole.Write(new Rule());
        AnsiConsole.MarkupLine("[red]Failed to launch the GUI. Falling back to console mode.[/]");
        AnsiConsole.Write(new Rule());
      }
    }

    var cli = new CommandApp<RAWebInstallerCommand>();
    return cli.Run(args);
  }


}
