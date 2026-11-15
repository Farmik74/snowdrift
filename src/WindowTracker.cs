using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Snowdrift
{
    internal sealed class WinInfo
    {
        public IntPtr Hwnd;
        public Rectangle Rect;
        public bool IsTaskbar;
        public string Class;
        public string Title;
        public int Z;
    }

    // Список видимых окон сверху вниз (в порядке z-order).
    internal static class WindowTracker
    {
        public static bool IsTaskbarClass(string cls)
        {
            return cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
        }

        public static List<WinInfo> Snapshot(HashSet<IntPtr> skip, bool withTitles)
        {
            var list = new List<WinInfo>();
            var sb = new StringBuilder(256);
            int rectSize = Marshal.SizeOf(typeof(RECT));

            Native.EnumWindowsProc cb = delegate(IntPtr h, IntPtr lp)
            {
                if (skip.Contains(h)) return true;
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;

                sb.Length = 0;
                Native.GetClassName(h, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW") return true;
                bool tb = IsTaskbarClass(cls);
                if (!tb && Native.GetWindowTextLength(h) == 0) return true;

                RECT r;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, rectSize) != 0)
                {
                    if (!Native.GetWindowRect(h, out r)) return true;
                }
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                if (w < 40 || hh < 20) return true;

                var wi = new WinInfo();
                wi.Hwnd = h;
                wi.Rect = new Rectangle(r.Left, r.Top, w, hh);
                wi.IsTaskbar = tb;
                wi.Class = cls;
                if (withTitles)
                {
                    sb.Length = 0;
                    Native.GetWindowText(h, sb, sb.Capacity);
                    wi.Title = sb.ToString();
                }
                list.Add(wi);
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            for (int i = 0; i < list.Count; i++) list[i].Z = i;
            return list;
        }

        public static bool IsFullscreenOn(Rectangle screen, HashSet<IntPtr> skip)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || skip.Contains(fg)) return false;
            var sb = new StringBuilder(256);
            Native.GetClassName(fg, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || IsTaskbarClass(cls)) return false;
            RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        }
    }
}
