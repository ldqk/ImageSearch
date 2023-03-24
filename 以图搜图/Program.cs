using Masuit.Tools.Logging;
using System.Diagnostics;
using 以图搜图;

Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
AppDomain.CurrentDomain.UnhandledException += (sender, e) => LogManager.Error((Exception)e.ExceptionObject);
ApplicationConfiguration.Initialize();
Application.Run(new Form1());