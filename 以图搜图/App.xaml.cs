using System.Diagnostics;
using System.Windows;
using Masuit.Tools.Logging;

namespace 以图搜图;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "ImageSearch_SingleInstance_Mutex";

    protected override void OnStartup(StartupEventArgs e)
    {
#if !DEBUG
        // 检查单实例
        _mutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // 应用已在运行，激活现有实例并退出
            ActivateExistingWindow();
            Current.Shutdown();
            return;
        }
#endif

        base.OnStartup(e);

        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

        // 处理未捕获的异常
        DispatcherUnhandledException += (sender, args) =>
        {
            LogManager.Error(args.Exception);
            var owner = Current.MainWindow;
            if (owner != null)
            {
                MessageBox.Show(owner, args.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(args.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        // 处理非UI线程异常
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            LogManager.Error((Exception)args.ExceptionObject);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    private static void ActivateExistingWindow()
    {
        try
        {
            // 查找现有的应用程序进程
            var currentProcess = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(currentProcess.ProcessName);

            if (processes.Length > 1)
            {
                // 找到其他实例，激活其主窗口
                var existingProcess = processes.FirstOrDefault(p => p.Id != currentProcess.Id);
                if (existingProcess != null)
                {
                    var mainWindowHandle = existingProcess.MainWindowHandle;
                    if (mainWindowHandle != IntPtr.Zero)
                    {
                        // 显示窗口
                        if (NativeMethods.IsIconic(mainWindowHandle))
                        {
                            NativeMethods.ShowWindow(mainWindowHandle, 9); // 恢复窗口
                        }

                        // 激活窗口
                        NativeMethods.SetForegroundWindow(mainWindowHandle);
                        NativeMethods.BringWindowToTop(mainWindowHandle);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Error(ex);
        }
    }
}

// Windows API 互操作
public static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);
}