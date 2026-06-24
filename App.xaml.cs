using System.Windows;

namespace BlexAutoClicker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
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
