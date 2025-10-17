using System.Diagnostics;
using System.Windows;
using Masuit.Tools.Logging;

namespace 以图搜图;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

    // 处理未捕获的异常
    DispatcherUnhandledException += (sender, args) =>
    {
      LogManager.Error(args.Exception);
      MessageBox.Show(args.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
      args.Handled = true;
    };

    // 处理非UI线程异常
    AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
    {
      LogManager.Error((Exception)args.ExceptionObject);
    };
  }
}
