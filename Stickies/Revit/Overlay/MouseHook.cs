using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Stickies.Infrastructure;

namespace Stickies.Revit.Overlay
{
    /// <summary>
    /// Notices left clicks on markers and cards. Revit has no click event for views, so a mouse hook watches Revit's UI
    /// thread only (WH_MOUSE, not a system-wide hook). A press that lands on a marker or card belongs to Stickies: the
    /// press and its release are kept from Revit (so it does not also select or edit the element underneath) and
    /// reported as a click. Every other mouse message passes through untouched. A release more than a few pixels from
    /// the press is a drag, not a click.
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE = 7;
        private const int HC_ACTION = 0;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int ClickTolerance = 4;

        private readonly HookProc _proc; // kept referenced: the hook calls it from native code
        private readonly Func<int, int, bool> _isTarget;
        private readonly Action<int, int> _clicked;
        private IntPtr _hook;
        private int _downX, _downY;
        private bool _pressed;

        /// <param name="isTarget">Whether a screen point is on a marker or card (decided when the button goes down).</param>
        /// <param name="clicked">Called with the screen point after a click on a target.</param>
        public MouseHook(Func<int, int, bool> isTarget, Action<int, int> clicked)
        {
            _isTarget = isTarget;
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
                    if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK || msg == WM_LBUTTONUP)
                    {
                        var info = (MOUSEHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(MOUSEHOOKSTRUCT));
                        if (msg != WM_LBUTTONUP)
                        {
                            // A double click arrives as WM_LBUTTONDBLCLK in place of the second press.
                            _pressed = _isTarget(info.pt.X, info.pt.Y);
                            _downX = info.pt.X;
                            _downY = info.pt.Y;
                            if (_pressed) return (IntPtr)1; // ours: Revit does not see it
                        }
                        else if (_pressed)
                        {
                            _pressed = false;
                            if (Math.Abs(info.pt.X - _downX) <= ClickTolerance && Math.Abs(info.pt.Y - _downY) <= ClickTolerance)
                                _clicked(_downX, _downY);
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _pressed = false;
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
