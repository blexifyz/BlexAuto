using BlexAutoClicker.Services;

namespace BlexAutoClicker
{
    public static class ServiceLocator
    {
        private static Dictionary<Type, object> _services = new();

        public static void RegisterServices()
        {
            _services[typeof(MouseClickService)] = new MouseClickService();
            _services[typeof(HotkeyService)] = new HotkeyService();
            _services[typeof(ClickerEngineService)] = new ClickerEngineService();
            _services[typeof(UpdateService)] = new UpdateService();
            _services[typeof(FastFlagService)] = new FastFlagService();
        }

        public static T GetService<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var service))
                return (T)service;
            throw new InvalidOperationException($"Service {typeof(T).Name} not registered");
        }

        public static void CleanupServices()
        {
            foreach (var service in _services.Values)
            {
                if (service is IDisposable disposable)
                    disposable.Dispose();
            }
            _services.Clear();
        }
    }
}
