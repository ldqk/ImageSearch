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
    
    // 设置 Owner 为主窗口，确保对话框显示在主窗口上层
    Owner = Application.Current.MainWindow;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}
