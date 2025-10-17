using System.Windows;

namespace 以图搜图;

public partial class ErrorsDialog : Window
{
  public string ErrorMessage { get; set; }

  public ErrorsDialog(string errorMessage)
  {
    InitializeComponent();
    ErrorMessage = errorMessage;
    DataContext = this;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}
