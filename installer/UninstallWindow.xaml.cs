using System.IO;
using System.Windows;
using Spectre.Console;

namespace RAWebInstaller
{
  public partial class UninstallWindow : Window
  {

    readonly string PathToUninstall = "";
    readonly string RegistryKeyName = "";
    public UninstallWindow(string title, string pathToUninstall, string uninstallId)
    {
      InitializeComponent();
      Title = title;
      MessageTitle.Text = title;
      PathToUninstall = pathToUninstall;
      RegistryKeyName = $@"RAWeb_{uninstallId.Replace('\\', '_')}";
    }

    private void SetStatus(string status, bool showProgress)
    {
      StatusPanel.Margin = !string.IsNullOrEmpty(status) ? new Thickness(24, 16, 24, 24) : new Thickness(0);
      LogBox.Text = status;
      ProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
      bool keepAppData = !(RemoveAppDataCheckBox.IsChecked ?? false);

      try
      {
        UninstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        SetStatus("Uninstalling...", true);
        OSHelpers.TakeControl(PathToUninstall);

        var tempDir = OSHelpers.GetTempFolder();
        string appDataDir = Path.Combine(PathToUninstall, "App_Data");

        SetStatus("Removing installation folder...", true);
        foreach (string file in Directory.GetFiles(PathToUninstall))
        {
          if (!Path.GetFileName(file).Equals("install_raweb.exe", StringComparison.OrdinalIgnoreCase))
          {
            File.Delete(file);
          }
        }
        foreach (string dir in Directory.GetDirectories(PathToUninstall))
        {
          if (keepAppData && dir.Equals(appDataDir, StringComparison.OrdinalIgnoreCase))
          {
            continue;
          }
          Directory.Delete(dir, true);
        }

        // delete the registry uninstall key
        SetStatus("Removing registry entries...", true);
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{RegistryKeyName}", true))
        {
          if (key != null)
          {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{RegistryKeyName}");
          }
        }

        SetStatus("Uninstalling...", true);
        await Task.Delay(1000);

        UninstallButton.Visibility = Visibility.Collapsed;
        CancelButton.IsEnabled = true;
        CancelButton.Content = "Close";
        RemoveAppDataCheckBox.Visibility = Visibility.Collapsed;
        MessageTitle.Text = "Uninstall Complete";
        MessageText.Text = "The application has been successfully uninstalled.";
        SetStatus("", false);



      }
      catch (Exception ex)
      {
        ThemedMessageBox.Show(this, $"Installation failed. See log for details.", "Error");
        OSHelpers.ShowConsoleWindow();
        AnsiConsole.Write("\n");
        AnsiConsole.WriteException(ex);
        AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
        Console.ReadKey(true);
      }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }
  }
}
