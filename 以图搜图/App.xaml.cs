using System.Diagnostics;
using System.Windows;
using Masuit.Tools.Files;
using Masuit.Tools.Logging;
using 以图搜图.WebAPI;

namespace 以图搜图;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "ImageSearch_SingleInstance_Mutex";

    protected override void OnStartup(StartupEventArgs e)
    {
        var isAdmin = new IniFile("config.ini").GetValue("Global", "RunAsAdmin", false);
        if (isAdmin && !IsRunAsAdmin())
        {
            // 以管理员权限重新启动应用程序
            var exeName = Process.GetCurrentProcess().MainModule?.FileName;
            if (exeName != null)
            {
                var startInfo = new ProcessStartInfo(exeName)
                {
                    UseShellExecute = true,
                    Verb = "runas" // 提升权限
                };
                try
                {
                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    LogManager.Error(ex);
                    MessageBox.Show("需要管理员权限才能运行此应用程序。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Current.Shutdown();
                return;
            }
        }

        WebApiStartup.Run(e.Args);
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
        WebApiStartup.Stop().Wait();
    }

    public static bool IsRunAsAdmin()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
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