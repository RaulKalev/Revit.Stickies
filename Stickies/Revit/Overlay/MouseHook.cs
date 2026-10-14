using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Stickies.Infrastructure;

namespace Stickies.Revit.Overlay
{
    /// <summary>
    /// Notices left clicks in Revit's windows. Revit has no click event for views, so a mouse hook watches this thread
    /// only (WH_MOUSE on Revit's UI thread – not a system-wide hook). It never swallows input: every message is passed
    /// on, and the callback runs after the click, outside the hook. A press and release more than a few pixels apart
    /// is a drag (pan, window select), not a click.
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE = 7;
        private const int HC_ACTION = 0;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int ClickTolerance = 4;

        private readonly HookProc _proc; // kept referenced: the hook calls it from native code
        private readonly Action<int, int> _clicked;
        private IntPtr _hook;
        private int _downX, _downY;
        private bool _down;

        public MouseHook(Action<int, int> clicked)
        {
            _clicked = clicked;
            _proc = OnMouse;
            _hook = SetWindowsHookEx(WH_MOUSE, _proc, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero)
                AppLog.Warn("Marker clicks are not available: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code == HC_ACTION)
                {
                    var msg = message.ToInt32();
                    if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP)
                    {
                        var info = (MOUSEHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(MOUSEHOOKSTRUCT));
                        if (msg == WM_LBUTTONDOWN)
                        {
                            _down = true;
                            _downX = info.pt.X;
                            _downY = info.pt.Y;
                        }
                        else if (_down)
                        {
                            _down = false;
                            if (Math.Abs(info.pt.X - _downX) <= ClickTolerance && Math.Abs(info.pt.Y - _downY) <= ClickTolerance)
                                _clicked(info.pt.X, info.pt.Y);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Mouse hook", ex);
            }
            return CallNextHookEx(_hook, code, message, data);
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEHOOKSTRUCT
        {
            public POINT pt;
            public IntPtr hwnd;
            public uint wHitTestCode;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
