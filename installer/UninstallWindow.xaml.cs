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

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
      SetStatus("Uninstalling...", true);
      UninstallButton.IsEnabled = false;
      CancelButton.IsEnabled = false;

      try
      {
        bool keepAppData = !(RemoveAppDataCheckBox.IsChecked ?? false);
        await Task.Run(() =>
        {
          UninstallHelper.Uninstall(UninstallId, keepAppData, (status, showProgress) =>
          {
            // Marshal status updates back to UI thread
            Dispatcher.Invoke(() => SetStatus(status, showProgress));
            return null;
          });
        });

        MessageTitle.Text = "Uninstall Complete";
        MessageText.Text = "The application has been successfully uninstalled.";
        CancelButton.Content = "Close";
        UninstallButton.Visibility = Visibility.Collapsed;
        RemoveAppDataCheckBox.Visibility = Visibility.Collapsed;
      }
      catch (FileNotFoundException ex)
      {
        ThemedMessageBox.Show(this, ex.Message, "Error");
        UninstallButton.IsEnabled = true;
        SetStatus("", false);
      }
      catch (Exception ex)
      {
        ThemedMessageBox.Show(this, $"Uninstall failed. See log for details.", "Error");
        OSHelpers.ShowConsoleWindow();
        AnsiConsole.Write("\n");
        AnsiConsole.WriteException(ex);
        AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
        Console.ReadKey(true);
      }

      CancelButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }
  }
}
