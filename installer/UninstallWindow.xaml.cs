using System.IO;
using System.Windows;
using System.Windows.Controls;
using Spectre.Console;

namespace RAWebInstaller
{
  public partial class UninstallWindow : Window
  {
    readonly string UninstallId = "";
    public UninstallWindow(string title, string uninstallId)
    {
      InitializeComponent();
      Title = title;
      MessageTitle.Text = title;
      UninstallId = uninstallId;
    }

    private bool? SetStatus(string status, bool showProgress)
    {
      StatusPanel.Margin = !string.IsNullOrEmpty(status) ? new Thickness(24, 16, 24, 24) : new Thickness(0);
      LogBox.Text = status;
      ProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
      return null;
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
      SetStatus("Uninstalling...", true);
      UninstallButton.IsEnabled = false;
      CancelButton.IsEnabled = false;

      try
      {
        bool keepAppData = !(RemoveAppDataCheckBox.IsChecked ?? false);
        UninstallHelper.Uninstall(UninstallId, keepAppData, SetStatus);
      }
      catch (FileNotFoundException ex)
      {
        ThemedMessageBox.Show(this, ex.Message, "Error");
        UninstallButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        return;
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

      UninstallButton.Visibility = Visibility.Collapsed;
      CancelButton.IsEnabled = true;
      CancelButton.Content = "Close";
      RemoveAppDataCheckBox.Visibility = Visibility.Collapsed;
      MessageTitle.Text = "Uninstall Complete";
      MessageText.Text = "The application has been successfully uninstalled.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }
  }
}
