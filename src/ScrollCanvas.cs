using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class ViewportDrawing
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        internal static void Refresh(Control viewport)
        {
            viewport.Invalidate(true);
            // Flush paint during thumb tracking without pumping unrelated input or timers.
            if (viewport.IsHandleCreated && viewport.Visible)
                RedrawWindow(viewport.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100);
        }
    }
    internal sealed class ScrollSurface : Panel
    {
        internal ScrollSurface() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            if (e.NewValue != e.OldValue) ViewportDrawing.Refresh(this);
        }
    }
    // An editor viewport with themed scroll chrome; all fields stay alive while scrolling.
    internal class ScrollCanvas : UserControl
    {
        protected readonly Panel Canvas = new ScrollSurface();
        private readonly ScrollRail rail = new ScrollRail();
        private int offset;
        private bool arranging;
        private int contentUpdates;
        protected void BeginContentUpdate()
        {
            contentUpdates++; SuspendLayout(); Canvas.SuspendLayout();
        }
        protected void EndContentUpdate()
        {
            // Layout callbacks stay suppressed until both containers have settled.
            Canvas.ResumeLayout(true); ResumeLayout(true);
            contentUpdates--; if (contentUpdates == 0) Arrange();
        }
        internal ScrollCanvas()
        {
            AutoScroll = false; Controls.Add(Canvas); Controls.Add(rail);
            Canvas.ControlAdded += delegate(object sender, ControlEventArgs e) { Watch(e.Control); Arrange(); };
            Canvas.Layout += delegate { Arrange(); };
            rail.ScrollTo = delegate(int value) { SetOffset(value); };
            rail.TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        private void Watch(Control control)
        {
            control.Enter += delegate { Reveal(control); };
            control.ControlAdded += delegate(object sender, ControlEventArgs e) { Watch(e.Control); };
            foreach (Control child in control.Controls) Watch(child);
        }
        private void Reveal(Control control)
        {
            if (!control.IsHandleCreated || !Visible || control.Height > ClientSize.Height) return;
            int top = Canvas.PointToClient(control.PointToScreen(Point.Empty)).Y;
            if (top < offset) SetOffset(top - 8);
            else if (top + control.Height > offset + ClientSize.Height) SetOffset(top + control.Height - ClientSize.Height + 8);
        }
        public new Point AutoScrollPosition { get { return new Point(0, -offset); } set { SetOffset(Math.Max(0, value.Y)); } }
        internal void ScrollBy(int amount) { SetOffset(offset + amount); }
        private void SetOffset(int value)
        {
            int next = Math.Max(0, Math.Min(Math.Max(0, Canvas.Height - ClientSize.Height), value));
            if (next == offset) return;
            offset = next;
            Canvas.Top = -offset; rail.Offset = offset; rail.Invalidate();
            // Moving a large child can leave copied pixels in a clipped viewport.
            // Repaint its visible children after every scroll instead of retaining them.
            ViewportDrawing.Refresh(this);
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Arrange(); }
        private void Arrange()
        {
            if (arranging || contentUpdates > 0 || Canvas == null || rail == null) return;
            arranging = true;
            try
            {
                float scale = 1F; if (IsHandleCreated) using (var g = CreateGraphics()) scale = g.DpiX / 96F;
                int railWidth = (int)(14 * scale);
                int total = Canvas.Controls.Cast<Control>().Select(c => c.Bottom).DefaultIfEmpty(0).Max() + (int)(8 * scale);
                rail.Visible = total > ClientSize.Height;
                Canvas.SetBounds(0, -offset, Math.Max(1, ClientSize.Width - railWidth), Math.Max(total, ClientSize.Height));
                rail.SetBounds(ClientSize.Width - railWidth, 0, railWidth, ClientSize.Height);
                rail.Total = Canvas.Height; rail.Viewport = ClientSize.Height; SetOffset(offset);
                rail.Offset = offset; rail.Invalidate();
            }
            finally { arranging = false; }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta / 120 * 48);
            var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
        }
        private sealed class ScrollRail : Control
        {
            internal int Total, Viewport, Offset;
            internal Action<int> ScrollTo;
            private bool dragging, hover;
            private float grab;
            internal ScrollRail() { DoubleBuffered = true; Cursor = Cursors.Hand; }
            private RectangleF Thumb
            {
                get { float height = Math.Min(Height, Math.Max(32, Height * Viewport / (float)Math.Max(1, Total)));
                    return new RectangleF(4, (Height - height) * Offset / Math.Max(1, Total - Viewport), Math.Max(2, Width - 8), height); }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Height < 2) return;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = Palette.Round(Thumb, 4))
                using (var brush = new SolidBrush(dragging || hover ? Palette.Accent : Palette.SwitchOff)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                var thumb = Thumb; dragging = true; Capture = true;
                grab = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2;
                MoveThumb(e.Y); Invalidate();
            }
            private void MoveThumb(int y) { if (ScrollTo != null) ScrollTo((int)((y - grab) / Math.Max(1, Height - Thumb.Height) * (Total - Viewport))); }
            protected override void OnMouseMove(MouseEventArgs e) { if (dragging) MoveThumb(e.Y); }
            protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); }
            protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        }
    }
}
