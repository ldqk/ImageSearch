using Masuit.Tools.Logging;
using System.Diagnostics;

namespace 以图搜图;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

        //处理未捕获的异常
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        //处理UI线程异常
        Application.ThreadException += (sender, e) =>
        {
            LogManager.Error(e.Exception);
            MessageBox.Show(e.Exception.Message);
        };

        //处理非UI线程异常
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => LogManager.Error((Exception)e.ExceptionObject);

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}