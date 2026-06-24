using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace BlexAutoClicker.Services
{
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        public const int WM_HOTKEY = 0x0312;

        private static readonly int[] MouseButtonVKs = { 0x04, 0x05, 0x06 };
        private static readonly string[] MouseButtonNames = { "MButton", "X1", "X2" };

        private IntPtr _windowHandle;
        private Dictionary<int, HotkeyEntry> _hotkeys = new();
        private int _nextId = 9000;

        private IntPtr _kbHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _kbProc;
        private Action<int>? _rawKeyDown;
        private Action<int>? _rawKeyUp;

        private class HotkeyEntry
        {
            public int Id;
            public Key Key;
            public ModifierKeys Modifiers;
            public Action? Callback;
            public int VirtualKey;
            public uint ModifierFlag;
        }

        public static bool IsMouseButtonKey(Key key)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            return vk >= 0x04 && vk <= 0x06;
        }

        public static string GetMouseButtonName(int vk) => vk switch
        {
            0x04 => "MButton",
            0x05 => "X1",
            0x06 => "X2",
            _ => ""
        };

        public void SetWindowHandle(IntPtr hWnd) => _windowHandle = hWnd;

        public int RegisterHotkey(Key key, ModifierKeys modifiers, Action callback)
        {
            UnregisterHotkey(key);
            int id = _nextId++;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            uint mod = modifiers switch
            {
                ModifierKeys.Control => 0x0002,
                ModifierKeys.Shift => 0x0004,
                ModifierKeys.Alt => 0x0001,
                ModifierKeys.Windows => 0x0008,
                _ => 0x0000
            };

            if (vk >= 0x04 && vk <= 0x06)
            {
                var entry = new HotkeyEntry { Id = id, Key = key, Modifiers = modifiers, Callback = callback, VirtualKey = vk, ModifierFlag = mod };
                _hotkeys[id] = entry;
                return id;
            }

            if (RegisterHotKey(_windowHandle, id, mod, (uint)vk))
            {
                var entry = new HotkeyEntry { Id = id, Key = key, Modifiers = modifiers, Callback = callback, VirtualKey = vk, ModifierFlag = mod };
                _hotkeys[id] = entry;
                return id;
            }
            return -1;
        }

        public void UnregisterHotkey(Key key)
        {
            var match = _hotkeys.Values.FirstOrDefault(h => h.Key == key);
            if (match != null)
            {
                if (match.VirtualKey < 0x04 || match.VirtualKey > 0x06)
                    UnregisterHotKey(_windowHandle, match.Id);
                _hotkeys.Remove(match.Id);
            }
        }

        public void UnregisterAll()
        {
            foreach (var entry in _hotkeys.Values)
            {
                if (entry.VirtualKey < 0x04 || entry.VirtualKey > 0x06)
                    UnregisterHotKey(_windowHandle, entry.Id);
            }
            _hotkeys.Clear();
        }

        public bool HandleHotkeyMessage(int id)
        {
            if (_hotkeys.TryGetValue(id, out var entry))
            {
                entry.Callback?.Invoke();
                return true;
            }
            return false;
        }

        public int GetHotkeyId(Key key) => _hotkeys.Values.FirstOrDefault(h => h.Key == key)?.Id ?? -1;

        public void InstallKeyboardHook(Action<int> keyDown, Action<int> keyUp)
        {
            _rawKeyDown = keyDown;
            _rawKeyUp = keyUp;

            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule!;
            IntPtr modHandle = GetModuleHandle(curModule.ModuleName);

            _kbProc = KbHookCallback;
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, modHandle, 0);
        }

        public void UninstallKeyboardHook()
        {
            if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            _rawKeyDown = null;
            _rawKeyUp = null;
        }

        /// <summary>
        /// Call this periodically (e.g. every 100-200ms) to detect mouse button hotkey presses.
        /// Returns the VK of any mouse button that was just pressed (Rising edge via LSB state flag).
        /// Uses GetAsyncKeyState bit-0 so quick press-release between polls is not missed.
        /// </summary>
        public int PollMouseButtonPress()
        {
            for (int i = 0; i < MouseButtonVKs.Length; i++)
            {
                short state = GetAsyncKeyState(MouseButtonVKs[i]);
                if ((state & 0x0001) != 0)
                {
                    return MouseButtonVKs[i];
                }
            }
            return 0;
        }

        /// <summary>
        /// Returns true if a mouse button (VK 0x04-0x06) is physically held down right now.
        /// </summary>
        public bool IsMouseButtonDown(int vk)
        {
            if (vk < 0x04 || vk > 0x06) return false;
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// Returns true if any key (by VK code) is physically held down right now.
        /// </summary>
        public bool IsKeyDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// Check if any mouse button hotkey callback should fire. Call from UI timer.
        /// </summary>
        public void CheckMouseHotkeys()
        {
            int vk = PollMouseButtonPress();
            if (vk > 0)
            {
                var match = _hotkeys.Values.FirstOrDefault(h => h.VirtualKey == vk);
                match?.Callback?.Invoke();
            }
        }

        private IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (wParam == (IntPtr)WM_KEYDOWN)
                    _rawKeyDown?.Invoke(vk);
                else if (wParam == (IntPtr)WM_KEYUP)
                    _rawKeyUp?.Invoke(vk);
            }
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            UninstallKeyboardHook();
            UnregisterAll();
        }
    }
}
