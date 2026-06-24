using BlexAutoClicker.Models;

namespace BlexAutoClicker.Services
{
    public class ClickerEngineService
    {
        private readonly MouseClickService _mouseService;
        private CancellationTokenSource? _cts;
        private Task? _clickTask;

        public ClickerStatistics Statistics { get; } = new();
        public bool IsRunning { get; private set; }

        public double CPS { get; set; } = 10.0;
        public double DutyCyclePercent { get; set; } = 50.0;
        public string MouseButton { get; set; } = "Left";
        public string ActivationMode { get; set; } = "Toggle";

        public event Action<bool>? StateChanged;
        public event Action? StatsUpdated;

        public ClickerEngineService()
        {
            _mouseService = new MouseClickService();
        }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            Statistics.StartTime = DateTime.Now;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _clickTask = Task.Run(() => ClickLoop(token));
            StateChanged?.Invoke(true);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _cts?.Cancel();
            _clickTask = null;
            StateChanged?.Invoke(false);
        }

        public void ResetStats()
        {
            Statistics.Reset();
            StatsUpdated?.Invoke();
        }

        public void UpdateStatsNow() => StatsUpdated?.Invoke();

        private void ClickLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int totalDelay = (int)Math.Max(1, 1000.0 / Math.Max(1.0, CPS));
                int holdMs = (int)Math.Max(1, totalDelay * DutyCyclePercent / 100.0);
                int releaseMs = Math.Max(1, totalDelay - holdMs);

                _mouseService.HoldDown(MouseButton);
                Thread.Sleep(holdMs);
                _mouseService.Release(MouseButton);

                Statistics.RecordClick();

                if (releaseMs > 0 && !token.IsCancellationRequested)
                    Thread.Sleep(releaseMs);

                if (token.IsCancellationRequested) break;

                if (Statistics.TotalClicks % 5 == 0)
                    StatsUpdated?.Invoke();
            }

            IsRunning = false;
        }
    }
}
