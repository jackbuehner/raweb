using Spectre.Console.Cli;
using RAWebInstaller;
using System.Windows;

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
      // wait 5 seconds for debugging
      // Thread.Sleep(5000);

      // hide the console window
      OSHelpers.HideConsoleWindow();

      var app = new Application();
      var wnd = new MainWindow();
      app.Run(wnd);
      return 0;
    }

    var cli = new CommandApp<RAWebInstallerCommand>();
    return cli.Run(args);
  }


}
