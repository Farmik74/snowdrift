using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Snowdrift
{
    // Прозрачное окно на весь монитор: поверх всех, клики проходят насквозь.
    internal sealed class Overlay : Form
    {
        public readonly Rectangle Area;
        IntPtr memDC, hBmp, oldBmp, bits;
        Bitmap bmp;
        Graphics g;
        public bool ShownEmpty;
        public IntPtr Bits { get { return bits; } }

        public Overlay(Rectangle area)
        {
            Area = area;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Snowdrift overlay";
            Location = area.Location;
            Size = area.Size;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // LAYERED | TRANSPARENT | TOPMOST | TOOLWINDOW | NOACTIVATE
                cp.ExStyle |= 0x80000 | 0x20 | 0x8 | 0x80 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void InitSurface()
        {
            var bi = new BITMAPINFOHEADER();
            bi.biSize = 40;
            bi.biWidth = Area.Width;
            bi.biHeight = -Area.Height; // сверху вниз
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            IntPtr screenDC = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screenDC);
            hBmp = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            Native.ReleaseDC(IntPtr.Zero, screenDC);
            oldBmp = Native.SelectObject(memDC, hBmp);
            bmp = new Bitmap(Area.Width, Area.Height, Area.Width * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
        }

        public Graphics Begin()
        {
            g.ResetTransform();
            g.Clear(Color.Transparent);
            g.TranslateTransform(-Area.Left, -Area.Top);
            return g;
        }

        public void Present()
        {
            g.Flush(FlushIntention.Sync);
            var dst = new POINT(Area.Left, Area.Top);
            var sz = new SIZE(Area.Width, Area.Height);
            var src = new POINT(0, 0);
            var bf = new BLENDFUNCTION();
            bf.BlendOp = 0;
            bf.BlendFlags = 0;
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = 1; // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref sz, memDC, ref src, 0, ref bf, 2);
        }

        public void PresentEmpty()
        {
            if (ShownEmpty) return;
            g.ResetTransform();
            g.Clear(Color.Transparent);
            Present();
            ShownEmpty = true;
        }

        public void KeepOnTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        protected override void Dispose(bool disposing)
        {
            if (g != null) { g.Dispose(); g = null; }
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (memDC != IntPtr.Zero)
            {
                Native.SelectObject(memDC, oldBmp);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }
}
