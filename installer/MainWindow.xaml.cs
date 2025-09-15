using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RAWebInstaller
{
  public partial class MainWindow : Window
  {

    readonly string defaultLocalHttpsUrl = $"https://{Environment.MachineName}/RAWeb";
    public MainWindow()
    {
      InitializeComponent();
      this.Icon = new BitmapImage(
        new Uri("wwwroot/lib/assets/icon.ico", UriKind.Relative));

      // check if RAWeb is already installed (or partially installed)
      // by seeing if the default installation directory exists
      string defaultInstallDir = $@"C:\Program Files\RAWeb\Default Web Site\RAWeb";
      string legacyInstallDir = $@"C:\inetpub\wwwroot\RAWeb";
      bool isAlreadyInstalled = Directory.Exists(defaultInstallDir) || Directory.Exists(legacyInstallDir);

      // set the app metadata
      var asm = Assembly.GetExecutingAssembly();
      var version = asm.GetName().Version?.ToString() ?? "unknown";
      AppTitleText.Text = isAlreadyInstalled ? "RAWeb is already installed" : "Install RAWeb?";
      PublisherText.Text = "Publisher: RAWeb";
      VersionText.Text = $"Version: {VersionInfo.GetVersionString()}";
      SourceText.Text = $"Source: {Environment.ProcessPath}";

      // set up the install/reinstall button
      InstallButton.Click += InstallButton_Click;
      InstallButtonDrop.Click += DropDown_Click;
      if (isAlreadyInstalled)
      {
        InstallButton.Content = "Reinstall (express)";
      }

      // set up the cancel button
      CancelButton.Click += CancelButton_Click;

      // set up the launch button
      if (isAlreadyInstalled)
      {
        CancelButton.Visibility = Visibility.Collapsed;
        LaunchButton.Visibility = Visibility.Visible;
        LaunchButton.Click += LaunchButton_Click;
      }
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
      WindowState = WindowState.Minimized;
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
      Close();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
      InstallButton.IsEnabled = false;
      InstallButtonDrop.IsEnabled = false;
      CancelButton.IsEnabled = false;
      LaunchButton.IsEnabled = false;
      LogBox.Text = "Starting installation...";
      ProgressBar.Visibility = Visibility.Visible;

      // run CLI in background so UI stays responsive
      await Task.Run(() =>
      {
        var consoleSettings = new AnsiConsoleSettings
        {
          Ansi = AnsiSupport.No,
          ColorSystem = ColorSystemSupport.NoColors,
          Interactive = InteractionSupport.No,
          Out = new AnsiConsoleOutput(new TextBoxWriter(LogBox)),
        };
        AnsiConsole.Console = AnsiConsole.Create(consoleSettings);
        var cli = new CommandApp<RAWebInstallerCommand>();
        cli.Run(["--express", "--exit-on-complete", "--install-iis"]);
      });

      ProgressBar.Visibility = Visibility.Collapsed;
      InstallButton.IsEnabled = true;
      InstallButtonDrop.IsEnabled = true;
      CancelButton.IsEnabled = true;
      LaunchButton.IsEnabled = true;

      // if the launch when ready ckechbox was checked before we finished,
      // we need to launch the browser to the local RAWeb URL
      if (LaunchWhenReady.IsChecked == true)
      {
        LaunchUrl(defaultLocalHttpsUrl);

        // close the GUI window
        Close();
        return;
      }

      // show the launch button instead of the cancel button
      CancelButton.Visibility = Visibility.Collapsed;
      LaunchButton.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
      Close(); // close the GUI window
    }

    private void DropDown_Click(object sender, RoutedEventArgs e)
    {
      var menu = new ContextMenu
      {
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        FontSize = 14,
      };

      // Create menu items
      var installLaunch = new MenuItem { Header = "Custom install in console" };
      installLaunch.Click += (s, a) =>
      {
        OSHelpers.ShowConsoleWindow();
        Close(); // close the GUI window
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings());
        var cli = new CommandApp<RAWebInstallerCommand>();
        cli.Run(["--express", "false"]);
      };

      var installOnly = new MenuItem { Header = "Express install in console" };
      installOnly.Click += (s, a) =>
      {
        OSHelpers.ShowConsoleWindow();
        Close(); // close the GUI window
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings());
        var cli = new CommandApp<RAWebInstallerCommand>();
        cli.Run(["--express", "--install-iis"]);
      };

      // Add items
      menu.Items.Add(installLaunch);
      menu.Items.Add(installOnly);

      // Place menu relative to button
      menu.PlacementTarget = InstallButtonDrop;
      menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
      menu.IsOpen = true;
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
      LaunchUrl(defaultLocalHttpsUrl);
      Close(); // close the GUI window
    }


    private static void LaunchUrl(string url)
    {
      try
      {
        // launch the default browser to the local RAWeb URL
        Process.Start(new ProcessStartInfo
        {
          FileName = url,
          UseShellExecute = true
        });
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to launch browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    // helper to force UI update during fake loop
    private void DoEvents()
    {
      Application.Current.Dispatcher.Invoke(
          System.Windows.Threading.DispatcherPriority.Background,
          new System.Action(delegate { }));
    }
  }

  public class TextBoxWriter : TextWriter
  {
    private readonly System.Windows.Controls.TextBlock _testBlock;
    private StringBuilder _buffer = new();
    public override Encoding Encoding => Encoding.UTF8;

    public TextBoxWriter(System.Windows.Controls.TextBlock testBlock)
    {
      _testBlock = testBlock;
    }

    public override void Write(char value)
    {
      if (value == '\n' || value == '\r')
      {
        FlushBuffer();
      }
      else
      {
        _buffer.Append(value);
      }
    }

    public override void Write(string? value)
    {
      if (string.IsNullOrEmpty(value))
        return;

      foreach (char c in value)
        Write(c);
    }

    public override void WriteLine(string? value)
    {
      if (!string.IsNullOrEmpty(value))
      {
        _buffer.Append(value);
      }
      FlushBuffer(); // commit completed line
    }

    private void FlushBuffer()
    {
      var line = _buffer.ToString();
      _buffer.Clear();

      if (string.IsNullOrWhiteSpace(line))
        return; // skip blank lines that cause "extra line"

      _testBlock.Dispatcher.BeginInvoke(() =>
      {
        _testBlock.Text = line;
      });
    }
  }
}
