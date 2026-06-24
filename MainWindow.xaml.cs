using BlexAutoClicker.Services;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BlexAutoClicker
{
    public partial class MainWindow : Window
    {
        private ClickerEngineService _engine = null!;
        private HotkeyService _hotkeys = null!;
        private HwndSource? _hwndSource;
        private System.Timers.Timer? _statsTimer;
        private bool _awaitingBind;
        private Action<Key>? _bindCallback;
        private bool _updatingCps;
        private bool _updatingDuty;
        private bool _prevStartBtnState;

        private static readonly Key MouseMiddleKey = (Key)0x04;
        private static readonly Key MouseX1Key = (Key)0x05;
        private static readonly Key MouseX2Key = (Key)0x06;

        private Key _startKey = Key.F6;
        private static readonly string ConfigPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, "config.json");

        private class Preset
        {
            public string Name { get; set; } = "";
            public double Cps { get; set; }
            public double Duty { get; set; }
        }

        private class AppConfig
        {
            public double CPS { get; set; } = 10.0;
            public double DutyCyclePercent { get; set; } = 50.0;
            public string MouseButton { get; set; } = "Left";
            public string ActivationMode { get; set; } = "Toggle";
            public int StartKeyVk { get; set; } = 0x75; // F6
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _engine = ServiceLocator.GetService<ClickerEngineService>();
                _hotkeys = ServiceLocator.GetService<HotkeyService>();

                _engine.StateChanged += OnStateChanged;

                _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                _hwndSource?.AddHook(WndProc);

                var hWnd = new WindowInteropHelper(this).Handle;
                _hotkeys.SetWindowHandle(hWnd);
                RegisterAllHotkeys();

                _statsTimer = new System.Timers.Timer(50);
                _statsTimer.Elapsed += (_, _) => Dispatcher.Invoke(() =>
                {
                    RefreshStats();
                    if (_awaitingBind)
                    {
                        int vk = _hotkeys.PollMouseButtonPress();
                        if (vk > 0)
                        {
                            Key mappedKey = VkToKey(vk);
                            CompleteBind(mappedKey);
                        }
                    }
                    else if (_engine.ActivationMode == "Hold")
                    {
                        int startVk = KeyToVk(_startKey);
                        bool down = _hotkeys.IsKeyDown(startVk);
                        if (down && !_prevStartBtnState)
                        {
                            if (!_engine.IsRunning) _engine.Start();
                        }
                        else if (!down && _prevStartBtnState)
                        {
                            _engine.Stop();
                        }
                        _prevStartBtnState = down;
                    }
                    else
                    {
                        _hotkeys.CheckMouseHotkeys();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
                _statsTimer.Start();

                LoadConfig();
                LoadPresets();
                UpdateUI(false);
                ServiceLocator.GetService<UpdateService>().CheckForUpdate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Init error: {ex.Message}", "Blex Auto", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegisterAllHotkeys()
        {
            if (_engine.ActivationMode != "Hold")
                _hotkeys.RegisterHotkey(_startKey, ModifierKeys.None, ToggleClicker);
        }

        private void OnRawKeyDown(int vkCode)
        {
            if (_awaitingBind)
            {
                Key mappedKey = VkToKey(vkCode);
                Dispatcher.Invoke(() => CompleteBind(mappedKey));
            }
        }

        private static Key VkToKey(int vk) => vk switch
        {
            0x04 => MouseMiddleKey,
            0x05 => MouseX1Key,
            0x06 => MouseX2Key,
            _ => KeyInterop.KeyFromVirtualKey(vk)
        };

        private static string KeyDisplayName(Key key)
        {
            if (key == MouseX1Key) return "X1";
            if (key == MouseX2Key) return "X2";
            if (key == MouseMiddleKey) return "MButton";
            if (key == Key.Escape) return "Esc";
            return key.ToString();
        }

        private static int KeyToVk(Key key)
        {
            if (key == MouseMiddleKey) return 0x04;
            if (key == MouseX1Key) return 0x05;
            if (key == MouseX2Key) return 0x06;
            return KeyInterop.VirtualKeyFromKey(key);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == HotkeyService.WM_HOTKEY)
                handled = _hotkeys.HandleHotkeyMessage(wParam.ToInt32());
            return IntPtr.Zero;
        }

        private void ToggleClicker()
        {
            Dispatcher.Invoke(() =>
            {
                if (_engine.IsRunning) _engine.Stop();
                else _engine.Start();
            });
        }

        private void ResetStatsBtn_Click(object sender, RoutedEventArgs e) => _engine?.ResetStats();

        private void OnStateChanged(bool running) => Dispatcher.Invoke(() => UpdateUI(running));

        private void UpdateUI(bool running)
        {
            if (running)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                StatusLabel.Text = "RUNNING";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                StatusLabel.Text = "STOPPED";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }

        private void RefreshStats()
        {
            if (!IsLoaded) return;
        }

        // ─── CPS Slider + TextBox ─────────────────────────────────────

        private void CpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingCps || !IsLoaded) return;
            _updatingCps = true;
            _engine.CPS = e.NewValue;
            CpsInputBox.Text = e.NewValue.ToString("F1");
            _updatingCps = false;
        }

        private void CpsInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingCps || !IsLoaded || CpsInputBox.IsKeyboardFocused == false) return;
            _updatingCps = true;
            if (double.TryParse(CpsInputBox.Text, out double val) && val >= 1 && val <= 100000)
            {
                _engine.CPS = val;
                if (val <= 100) CpsSlider.Value = val;
            }
            _updatingCps = false;
        }

        // ─── Duty Cycle Slider + TextBox ─────────────────────────────

        private void DutySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingDuty || !IsLoaded) return;
            _updatingDuty = true;
            _engine.DutyCyclePercent = e.NewValue;
            DutyInputBox.Text = e.NewValue.ToString("F1");
            _updatingDuty = false;
        }

        private void DutyInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingDuty || !IsLoaded || DutyInputBox.IsKeyboardFocused == false) return;
            _updatingDuty = true;
            if (double.TryParse(DutyInputBox.Text, out double val) && val >= 1 && val <= 100)
            {
                _engine.DutyCyclePercent = val;
                DutySlider.Value = val;
            }
            _updatingDuty = false;
        }

        // ─── Mouse Button + Activation ───────────────────────────────

        private void MouseBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            if (LeftRadio.IsChecked == true) _engine.MouseButton = "Left";
            else if (MiddleRadio.IsChecked == true) _engine.MouseButton = "Middle";
            else _engine.MouseButton = "Right";
        }

        private void Activation_Checked(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            _engine.ActivationMode = ToggleRadio.IsChecked == true ? "Toggle" : "Hold";
            SyncHoldStartState();
            RebindHotkey();
        }

        // ─── Keybind rebinding ───────────────────────────────────────

        private void StartKeyBtn_Click(object sender, RoutedEventArgs e) =>
            BeginBind(key => { _startKey = key; StartKeyBtn.Content = KeyDisplayName(key); SyncHoldStartState(); RebindHotkey(); });

        private void SyncHoldStartState()
        {
            int vk = KeyToVk(_startKey);
            _prevStartBtnState = _hotkeys.IsKeyDown(vk);
        }

        private void BeginBind(Action<Key> callback)
        {
            _awaitingBind = true;
            _bindCallback = callback;
            KeybindHint.Text = "Press any key or mouse button...";
            _hotkeys.InstallKeyboardHook(OnRawKeyDown, _ => { });
        }

        private void CompleteBind(Key key)
        {
            if (!_awaitingBind || _bindCallback == null) return;
            _awaitingBind = false;
            _hotkeys.UninstallKeyboardHook();
            _bindCallback(key);
            _bindCallback = null;
            KeybindHint.Text = "";
        }

        private void RebindHotkey()
        {
            _hotkeys.UnregisterAll();
            RegisterAllHotkeys();
        }

        // ─── Presets ─────────────────────────────────────────────────

        private void LoadPresets()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BlexAutoClicker.presets.json");
                if (stream == null) return;
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var presets = JsonSerializer.Deserialize<List<Preset>>(json);
                if (presets == null || presets.Count == 0) return;
                PresetsList.ItemsSource = presets;
            }
            catch { }
        }

        private void PresetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Preset preset)
            {
                _updatingCps = true;
                _updatingDuty = true;
                _engine.CPS = preset.Cps;
                _engine.DutyCyclePercent = preset.Duty;
                CpsInputBox.Text = preset.Cps.ToString("F2");
                DutyInputBox.Text = preset.Duty.ToString("F2");
                if (preset.Cps <= 100) CpsSlider.Value = preset.Cps;
                if (preset.Duty <= 100) DutySlider.Value = preset.Duty;
                _updatingCps = false;
                _updatingDuty = false;
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox tb && (tb.Name == "CpsInputBox" || tb.Name == "DutyInputBox"))
            {
                string before = tb.Text.Substring(0, tb.SelectionStart);
                string after = tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
                string result = before + e.Text + after;
                e.Handled = !Regex.IsMatch(result, @"^\d*\.?\d*$");
            }
            else
                e.Handled = !Regex.IsMatch(e.Text, "^[0-9]$");
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void AboutBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Blex Auto v1.1\nProfessional Auto Clicker",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg == null) return;

                _engine.CPS = cfg.CPS;
                _engine.DutyCyclePercent = cfg.DutyCyclePercent;
                _engine.MouseButton = cfg.MouseButton;
                _engine.ActivationMode = cfg.ActivationMode;

                CpsInputBox.Text = cfg.CPS.ToString("F1");
                if (cfg.CPS <= 100) CpsSlider.Value = cfg.CPS;
                DutyInputBox.Text = cfg.DutyCyclePercent.ToString("F1");
                if (cfg.DutyCyclePercent <= 100) DutySlider.Value = cfg.DutyCyclePercent;

                if (cfg.MouseButton == "Middle") MiddleRadio.IsChecked = true;
                else if (cfg.MouseButton == "Right") RightRadio.IsChecked = true;
                else LeftRadio.IsChecked = true;

                ToggleRadio.IsChecked = cfg.ActivationMode == "Toggle";
                HoldRadio.IsChecked = cfg.ActivationMode == "Hold";

                _startKey = VkToKey(cfg.StartKeyVk);
                StartKeyBtn.Content = KeyDisplayName(_startKey);

                RebindHotkey();
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var cfg = new AppConfig
                {
                    CPS = _engine.CPS,
                    DutyCyclePercent = _engine.DutyCyclePercent,
                    MouseButton = _engine.MouseButton,
                    ActivationMode = _engine.ActivationMode,
                    StartKeyVk = KeyToVk(_startKey)
                };
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _statsTimer?.Stop();
            _statsTimer?.Dispose();
            SaveConfig();
            _hotkeys?.Dispose();
            _engine?.Stop();
            _hwndSource?.RemoveHook(WndProc);
        }
    }
}
