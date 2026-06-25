using BlexAutoClicker.Services;
using System.Diagnostics;
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
        private System.Action<Key, ModifierKeys>? _bindCallback;
        private bool _updatingCps;
        private bool _updatingDuty;
        private bool _prevStartBtnState;

        private static readonly Key MouseMiddleKey = (Key)0x04;
        private static readonly Key MouseX1Key = (Key)0x05;
        private static readonly Key MouseX2Key = (Key)0x06;

        private Key _startKey = Key.F6;
        private ModifierKeys _startKeyModifiers = ModifierKeys.None;
        private static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string ConfigPath = Path.Combine(AppDir, "config.json");
        private static readonly string UserPresetsPath = Path.Combine(AppDir, "user_presets.json");

        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        private class Preset : System.ComponentModel.INotifyPropertyChanged
        {
            public string Name { get; set; } = "";
            public double Cps { get; set; }
            public double Duty { get; set; }

            private bool _isActive;
            public bool IsActive
            {
                get => _isActive;
                set { _isActive = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive))); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        private class AppConfig
        {
            public double CPS { get; set; } = 10.0;
            public double DutyCyclePercent { get; set; } = 50.0;
            public string MouseButton { get; set; } = "Left";
            public string ActivationMode { get; set; } = "Toggle";
            public int StartKeyVk { get; set; } = 0x75; // F6
            public int StartKeyModifiers { get; set; } = 0;
        }

        public MainWindow()
        {
            InitializeComponent();

            // Handle --apply-update: new exe replaces the old one after old process exits
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--apply-update" && i + 2 < args.Length)
                {
                    string targetExe = args[i + 1];
                    int oldPid = int.Parse(args[i + 2]);
                    string ourPath = Environment.ProcessPath ?? "";

                    // Retry copy until old process releases the file
                    for (int retry = 0; retry < 60; retry++)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(ourPath) && File.Exists(ourPath))
                            {
                                File.Copy(ourPath, targetExe, true);
                                Process.Start(new ProcessStartInfo { FileName = targetExe, UseShellExecute = true });
                            }
                            break;
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(1000);
                        }
                    }

                    Environment.Exit(0);
                }
            }

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
                    try
                    {
                        RefreshStats();
                        if (_awaitingBind)
                        {
                            int vk = _hotkeys?.PollMouseButtonPress() ?? 0;
                            if (vk > 0)
                            {
                                Key mappedKey = VkToKey(vk);
                                CompleteBind(mappedKey);
                            }
                        }
                        else if (_engine?.ActivationMode == "Hold")
                        {
                            int startVk = KeyToVk(_startKey);
                            bool modsHeld = ModifiersMatch(_startKeyModifiers);
                            bool down = modsHeld && (_hotkeys?.IsKeyDown(startVk) ?? false);
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
                            _hotkeys?.CheckMouseHotkeys();
                        }
                    }
                    catch (Exception ex)
                    {
                        try { File.AppendAllText(AppDir + "\\crash.log", $"[{DateTime.Now}] Timer: {ex}\n"); } catch { }
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
                _statsTimer.Start();

                LoadConfig();
                LoadPresets();
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                VersionText.Text = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v?";

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
                _hotkeys.RegisterHotkey(_startKey, _startKeyModifiers, ToggleClicker);
        }

        private void OnRawKeyDown(int vkCode)
        {
            if (_awaitingBind)
            {
                // Skip modifier-only presses — we check their state when a real key is pressed
                // General VKs: Shift(0x10), Ctrl(0x11), Alt(0x12), Win(0x5B/0x5C)
                // Left/Right variants: LShift(0xA0), RShift(0xA1), LCtrl(0xA2), RCtrl(0xA3), LAlt(0xA4), RAlt(0xA5)
                if (vkCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
                    return;

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

        private bool ModifiersMatch(ModifierKeys required)
        {
            if (_hotkeys == null) return false;
            bool shift = _hotkeys.IsKeyDown(0x10);
            bool ctrl = _hotkeys.IsKeyDown(0x11);
            bool alt = _hotkeys.IsKeyDown(0x12);
            return ((required & ModifierKeys.Shift) != 0) == shift
                && ((required & ModifierKeys.Control) != 0) == ctrl
                && ((required & ModifierKeys.Alt) != 0) == alt;
        }

        private static string KeyDisplayName(Key key, ModifierKeys mods)
        {
            string prefix = "";
            if ((mods & ModifierKeys.Control) != 0) prefix += "Ctrl + ";
            if ((mods & ModifierKeys.Shift) != 0) prefix += "Shift + ";
            if ((mods & ModifierKeys.Alt) != 0) prefix += "Alt + ";
            if (key == MouseX1Key) return prefix + "X1";
            if (key == MouseX2Key) return prefix + "X2";
            if (key == MouseMiddleKey) return prefix + "MButton";
            if (key == Key.Escape) return prefix + "Esc";
            return prefix + key.ToString();
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
            try
            {
                if (msg == HotkeyService.WM_HOTKEY)
                    handled = _hotkeys.HandleHotkeyMessage(wParam.ToInt32());
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(AppDir + "\\crash.log", $"[{DateTime.Now}] WndProc: {ex}\n"); } catch { }
            }
            return IntPtr.Zero;
        }

        private void ToggleClicker()
        {
            try
            {
                if (_engine == null) return;
                if (_engine.IsRunning) _engine.Stop();
                else _engine.Start();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(AppDir + "\\crash.log", $"[{DateTime.Now}] ToggleClicker: {ex}\n"); } catch { }
            }
        }

        private void ResetStatsBtn_Click(object sender, RoutedEventArgs e) => _engine?.ResetStats();

        private void OnStateChanged(bool running) => UpdateUI(running);

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
            ClearActivePresets();
            _updatingCps = true;
            _engine.CPS = e.NewValue;
            CpsInputBox.Text = e.NewValue.ToString("F1");
            _updatingCps = false;
        }

        private void CpsInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingCps || !IsLoaded || CpsInputBox.IsKeyboardFocused == false) return;
            ClearActivePresets();
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
            ClearActivePresets();
            _updatingDuty = true;
            _engine.DutyCyclePercent = e.NewValue;
            DutyInputBox.Text = e.NewValue.ToString("F1");
            _updatingDuty = false;
        }

        private void DutyInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingDuty || !IsLoaded || DutyInputBox.IsKeyboardFocused == false) return;
            ClearActivePresets();
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
            BeginBind((key, mods) => { _startKey = key; _startKeyModifiers = mods; StartKeyBtn.Content = KeyDisplayName(key, mods); SyncHoldStartState(); RebindHotkey(); });

        private void SyncHoldStartState()
        {
            int vk = KeyToVk(_startKey);
            _prevStartBtnState = _hotkeys.IsKeyDown(vk);
        }

        private void BeginBind(System.Action<Key, ModifierKeys> callback)
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
            _hotkeys?.UninstallKeyboardHook();

            // Detect modifier keys held at the time of the press
            var mods = ModifierKeys.None;
            if (_hotkeys?.IsKeyDown(0x10) == true) mods |= ModifierKeys.Shift;
            if (_hotkeys?.IsKeyDown(0x11) == true) mods |= ModifierKeys.Control;
            if (_hotkeys?.IsKeyDown(0x12) == true) mods |= ModifierKeys.Alt;

            _bindCallback(key, mods);
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
            // Global presets (hardcoded)
            var global = new List<Preset>
            {
                new Preset { Name = "Altify MS", Cps = 115.25, Duty = 54.45 },
                new Preset { Name = "Yikeswave MS", Cps = 135.00, Duty = 23.61 },
                new Preset { Name = "Wal MS", Cps = 115.25, Duty = 42.55 },
                new Preset { Name = "Mahad MS", Cps = 135.00, Duty = 23.61 }
            };
            PresetsList.ItemsSource = global;

            // User presets (from file)
            LoadUserPresets();
        }

        private void LoadUserPresets()
        {
            try
            {
                if (!File.Exists(UserPresetsPath)) return;
                var json = File.ReadAllText(UserPresetsPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var list = JsonSerializer.Deserialize<List<Preset>>(json, options);
                UserPresetsList.ItemsSource = list ?? new List<Preset>();
            }
            catch
            {
                UserPresetsList.ItemsSource = new List<Preset>();
            }
        }

        private void SaveUserPresets(List<Preset> presets)
        {
            var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserPresetsPath, json);
        }

        private void ClearActivePresets()
        {
            if (PresetsList.ItemsSource is List<Preset> globals)
                foreach (var p in globals) p.IsActive = false;
            if (UserPresetsList.ItemsSource is List<Preset> users)
                foreach (var p in users) p.IsActive = false;
        }

        private void PresetTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is Preset preset)
            {
                ClearActivePresets();
                preset.IsActive = true;

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

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var name = NewPresetName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter a preset name.", "Save Preset", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var presets = (UserPresetsList.ItemsSource as List<Preset>) ?? new List<Preset>();
            presets.Add(new Preset { Name = name, Cps = _engine.CPS, Duty = _engine.DutyCyclePercent });
            SaveUserPresets(presets);
            UserPresetsList.ItemsSource = null;
            UserPresetsList.ItemsSource = presets;
            NewPresetName.Clear();
        }

        private void DeletePreset_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is Preset preset)
            {
                var presets = (UserPresetsList.ItemsSource as List<Preset>) ?? new List<Preset>();
                presets.Remove(preset);
                SaveUserPresets(presets);
                UserPresetsList.ItemsSource = null;
                UserPresetsList.ItemsSource = presets;
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
            MessageBox.Show("Blex Auto v1.2\nProfessional Auto Clicker",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            ServiceLocator.GetService<UpdateService>().CheckForUpdate();
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
                _startKeyModifiers = (ModifierKeys)cfg.StartKeyModifiers;
                StartKeyBtn.Content = KeyDisplayName(_startKey, _startKeyModifiers);

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
                    StartKeyVk = KeyToVk(_startKey),
                    StartKeyModifiers = (int)_startKeyModifiers
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
