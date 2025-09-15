using System.Windows;

namespace RAWebInstaller
{
  public partial class ThemedMessageBox : Window
  {
    private ThemedMessageBox(string message, string title)
    {
      InitializeComponent();
      Title = title;
      MessageTitle.Text = title;
      MessageText.Text = message;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = true;
      Close();
    }

    public static bool Show(Window owner, string message, string title = "Message")
    {
      var box = new ThemedMessageBox(message, title);

      if (owner != null)
        box.Owner = owner;

      return box.ShowDialog() ?? false;
    }
  }
}
