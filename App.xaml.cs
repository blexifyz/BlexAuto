using System.IO;
using System.Windows;

namespace BlexAutoClicker
{
    public partial class App : Application
    {
        private static readonly string CrashLogPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
            "crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                File.AppendAllText(CrashLogPath, $"[{DateTime.Now}] UI: {args.Exception}\n");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                File.AppendAllText(CrashLogPath, $"[{DateTime.Now}] FATAL: {args.ExceptionObject}\n");
            };

            ServiceLocator.RegisterServices();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ServiceLocator.CleanupServices();
            base.OnExit(e);
        }
    }
}
