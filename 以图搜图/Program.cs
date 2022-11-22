using Masuit.Tools.Logging;
using System.Diagnostics;

namespace 以图搜图
{
	internal static class Program
	{
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main()
		{
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
			AppDomain.CurrentDomain.UnhandledException += (sender, e) => LogManager.Error((Exception)e.ExceptionObject);
			ApplicationConfiguration.Initialize();
			Application.Run(new Form1());
		}
	}
}
