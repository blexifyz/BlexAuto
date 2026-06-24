namespace BlexAutoClicker.Models
{
    public class ClickerStatistics
    {
        public long TotalClicks { get; set; } = 0;
        public DateTime StartTime { get; set; }
        public TimeSpan TotalRuntime { get; set; } = TimeSpan.Zero;
        public double CurrentCPS { get; set; } = 0;
        public List<DateTime> RecentClicks { get; set; } = new();

        public void RecordClick()
        {
            TotalClicks++;
            RecentClicks.Add(DateTime.Now);
            RecentClicks.RemoveAll(c => c < DateTime.Now.AddSeconds(-1));
            UpdateCurrentCPS();
        }

        public void UpdateCurrentCPS()
        {
            var oneSecondAgo = DateTime.Now.AddSeconds(-1);
            CurrentCPS = RecentClicks.Count(c => c > oneSecondAgo);
        }

        public void Reset()
        {
            TotalClicks = 0;
            StartTime = DateTime.Now;
            TotalRuntime = TimeSpan.Zero;
            CurrentCPS = 0;
            RecentClicks.Clear();
        }
    }
}