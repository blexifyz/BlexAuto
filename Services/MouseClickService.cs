using System.Runtime.InteropServices;

namespace BlexAutoClicker.Services
{
    public class MouseClickService
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        public void Click(string mouseButton = "Left")
        {
            HoldDown(mouseButton);
            Release(mouseButton);
        }

        public void HoldDown(string mouseButton = "Left")
        {
            mouse_event(GetDownFlag(mouseButton), 0, 0, 0, UIntPtr.Zero);
        }

        public void Release(string mouseButton = "Left")
        {
            mouse_event(GetUpFlag(mouseButton), 0, 0, 0, UIntPtr.Zero);
        }

        public (int X, int Y) GetMousePosition()
        {
            GetCursorPos(out POINT pt);
            return (pt.X, pt.Y);
        }

        private uint GetDownFlag(string btn) => btn.ToLower() switch
        {
            "right" => MOUSEEVENTF_RIGHTDOWN,
            "middle" => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN
        };

        private uint GetUpFlag(string btn) => btn.ToLower() switch
        {
            "right" => MOUSEEVENTF_RIGHTUP,
            "middle" => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP
        };
    }
}
