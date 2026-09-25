using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class Palette
    {
        private static readonly Color[] light = {
            Color.FromArgb(246,247,249), Color.FromArgb(237,240,245), Color.White,
            Color.FromArgb(221,226,234), Color.FromArgb(28,35,48), Color.FromArgb(99,110,128),
            Color.FromArgb(0,102,219), Color.FromArgb(233,242,255), Color.FromArgb(153,77,22), Color.FromArgb(255,245,230),
            Color.FromArgb(228,236,247), Color.FromArgb(211,223,240), Color.FromArgb(234,237,241),
            Color.FromArgb(178,207,246), Color.FromArgb(171,181,195),
            Color.FromArgb(0,101,216), Color.FromArgb(0,86,193), Color.FromArgb(0,69,162)
        };
        private static readonly Color[] dark = {
            Color.FromArgb(22,25,31), Color.FromArgb(28,32,40), Color.FromArgb(36,41,51),
            Color.FromArgb(65,74,90), Color.FromArgb(235,239,247), Color.FromArgb(163,175,194),
            Color.FromArgb(112,179,255), Color.FromArgb(35,57,84), Color.FromArgb(255,193,132), Color.FromArgb(62,45,32),
            Color.FromArgb(48,58,73), Color.FromArgb(59,73,92), Color.FromArgb(31,36,45),
            Color.FromArgb(64,110,164), Color.FromArgb(85,96,113),
            Color.FromArgb(30,103,211), Color.FromArgb(40,118,230), Color.FromArgb(24,83,176)
        };
        internal static bool Dark { get; private set; }
        private static Color At(int index) { return (Dark ? dark : light)[index]; }
        internal static Color Background { get { return At(0); } }
        internal static Color Sidebar { get { return At(1); } }
        internal static Color Card { get { return At(2); } }
        internal static Color Border { get { return At(3); } }
        internal static Color Text { get { return At(4); } }
        internal static Color Muted { get { return At(5); } }
        internal static Color Accent { get { return At(6); } }
        internal static Color Selected { get { return At(7); } }
        internal static Color Warning { get { return At(8); } }
        internal static Color WarningBackground { get { return At(9); } }
        internal static Color Hover { get { return At(10); } }
        internal static Color Pressed { get { return At(11); } }
        internal static Color Disabled { get { return At(12); } }
        internal static Color SelectedBorder { get { return At(13); } }
        internal static Color SwitchOff { get { return At(14); } }
        internal static Color Primary { get { return At(15); } }
        internal static Color PrimaryHover { get { return At(16); } }
        internal static Color PrimaryPressed { get { return At(17); } }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr handle, string app, string id);
        internal static void WindowChrome(Form form)
        {
            if (!form.IsHandleCreated) return;
            try { int value = Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref value, 4); }
            catch (DllNotFoundException) { }
        }
        internal static void NativeChrome(Control control)
        {
            if (!control.IsHandleCreated) return;
            try { SetWindowTheme(control.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch (DllNotFoundException) { }
        }
        internal static void ChromeTree(Control control)
        {
            var form = control as Form;
            if (form != null) WindowChrome(form);
            if (control is ScrollableControl || control is ComboBox || control is ListBox || control is NumericUpDown) NativeChrome(control);
            foreach (Control child in control.Controls) ChromeTree(child);
        }
        internal static void Apply(bool useDark)
        {
            if (Dark == useDark) return;
            var before = Dark ? dark : light; Dark = useDark;
            foreach (Form form in Application.OpenForms)
            {
                form.SuspendLayout(); Recolor(form, before); WindowChrome(form); form.ResumeLayout(true); form.Invalidate(true);
            }
        }
        private static Color Translate(Color color, Color[] before)
        {
            if (color == Color.Transparent) return color;
            for (int i = 0; i < before.Length; i++) if (color.ToArgb() == before[i].ToArgb()) return At(i);
            return color;
        }
        private static void Recolor(Control control, Color[] before)
        {
            control.BackColor = Translate(control.BackColor, before);
            control.ForeColor = Translate(control.ForeColor, before);
            var surface = control as Surface;
            if (surface != null) { surface.Fill = Translate(surface.Fill, before); surface.Stroke = Translate(surface.Stroke, before); }
            if (control is ScrollableControl || control is ComboBox || control is ListBox || control is NumericUpDown) NativeChrome(control);
            foreach (Control child in control.Controls) Recolor(child, before);
            control.Invalidate();
        }
        internal static Label Label(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, ForeColor = color, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoSize = false, BackColor = Color.Transparent, AutoEllipsis = true };
        }
        internal static Color ParentBackground(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            {
                var surface = parent as Surface;
                if (surface != null) return surface.Fill;
                if (parent.BackColor.A == 255) return parent.BackColor;
            }
            return Background;
        }
        internal static GraphicsPath Round(RectangleF bounds, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class FlatAction : Button
    {
        internal bool Primary;
        internal bool Selected;
        private bool hover;
        private bool pressed;
        private bool keyPressed;
        internal FlatAction(string text, bool primary)
        {
            Text = text; Primary = primary; Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat;
            Font = new Font("Microsoft YaHei UI", 9F); Height = 36;
            FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; BackColor = Color.Transparent;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; hover = ClientRectangle.Contains(e.Location); } Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) keyPressed = true; Invalidate(); base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { keyPressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = keyPressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { pressed = keyPressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Palette.ParentBackground(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Palette.ParentBackground(this));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool down = Enabled && ((pressed && hover) || keyPressed);
            Color fill = Primary ? (down ? Palette.PrimaryPressed : hover ? Palette.PrimaryHover : Palette.Primary)
                : down ? Palette.Pressed : hover ? Palette.Hover : Selected ? Palette.Selected : Palette.Card;
            if (!Enabled) fill = Palette.Disabled;
            Color color = !Enabled ? Palette.Muted : Primary ? Color.White : Selected ? Palette.Accent : Palette.Text;
            float scale = e.Graphics.DpiX / 96F;
            bool focus = Focused && ShowFocusCues && Enabled;
            using (var path = Palette.Round(new RectangleF(1, 1, Width - 2, Height - 2), 8 * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(focus ? Palette.Accent : Selected ? Palette.SelectedBorder : Primary && Enabled ? fill : hover && Enabled ? Palette.SelectedBorder : Palette.Border, focus ? 2 * scale : 1))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            int textPadding = Width < 40 * scale ? 0 : 4;
            var textBounds = new Rectangle(textPadding, down ? (int)scale : 0, Width - textPadding * 2, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (focus && Primary) using (var pen = new Pen(Color.White, scale))
                using (var path = Palette.Round(new RectangleF(4 * scale, 4 * scale, Width - 8 * scale, Height - 8 * scale), 5 * scale)) e.Graphics.DrawPath(pen, path);
        }
    }

    internal sealed class Surface : Panel
    {
        internal Color Fill = Palette.Card;
        internal Color Stroke = Palette.Border;
        internal Surface()
        {
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Palette.ParentBackground(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Palette.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), 12 * e.Graphics.DpiX / 96F))
            using (var brush = new SolidBrush(Fill))
            using (var pen = new Pen(Stroke))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            base.OnPaint(e);
        }
    }

    internal sealed class SwitchOption : CheckBox
    {
        internal SwitchOption()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float s = e.Graphics.DpiX / 96F;
            var track = new RectangleF(Width - 34 * s, (Height - 20 * s) / 2, 34 * s, 20 * s);
            using (var path = Palette.Round(track, 10 * s))
            using (var brush = new SolidBrush(Enabled && Checked ? Palette.Primary : Palette.SwitchOff)) e.Graphics.FillPath(brush, path);
            using (var brush = new SolidBrush(Color.White)) e.Graphics.FillEllipse(brush, track.X + (Checked ? 16 : 2) * s, track.Y + 2 * s, 16 * s, 16 * s);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - (int)(43 * s), Height), Enabled ? ForeColor : Palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    }

    internal sealed class BrandMark : Control
    {
        internal BrandMark() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            AppIcon.DrawMark(e.Graphics, Math.Min(Width, Height), Palette.Primary);
        }
    }

    internal sealed class DeviceGlyph : Control
    {
        internal int Flow;
        internal bool Active;
        internal DeviceGlyph() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.ScaleTransform(Width / 44F, Height / 44F);
            using (var path = Palette.Round(new RectangleF(.5F, .5F, 43, 43), 12))
            using (var brush = new SolidBrush(Active ? Palette.Selected : Palette.Background)) e.Graphics.FillPath(brush, path);
            using (var pen = new Pen(Active ? Palette.Accent : Palette.Muted, 1.8F))
            using (var symbol = new GraphicsPath())
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (Flow == 0)
                {
                    symbol.AddPolygon(new[] { new Point(12, 18), new Point(17, 18), new Point(23, 13), new Point(23, 31), new Point(17, 26), new Point(12, 26) });
                    symbol.StartFigure(); symbol.AddArc(20, 15, 12, 14, -65, 130);
                    symbol.StartFigure(); symbol.AddArc(20, 11, 19, 22, -60, 120);
                }
                else
                {
                    using (var path = Palette.Round(new RectangleF(18, 10, 9, 18), 4.5F)) symbol.AddPath(path, false);
                    symbol.StartFigure(); symbol.AddArc(14, 16, 17, 16, 0, 180);
                    symbol.StartFigure(); symbol.AddLine(22.5F, 32, 22.5F, 36);
                    symbol.StartFigure(); symbol.AddLine(18, 36, 27, 36);
                }
                var bounds = symbol.GetBounds();
                using (var move = new Matrix()) {
                    move.Translate(22 - (bounds.Left + bounds.Right) / 2, 22 - (bounds.Top + bounds.Bottom) / 2);
                    symbol.Transform(move);
                }
                e.Graphics.DrawPath(pen, symbol);
            }
        }
    }

    internal sealed class SectionTabs : UserControl
    {
        private readonly FlowLayoutPanel headings = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false };
        private readonly Surface body = new Surface { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 6) };
        private readonly System.Collections.Generic.List<Control> pages = new System.Collections.Generic.List<Control>();
        private readonly System.Collections.Generic.List<FlatAction> buttons = new System.Collections.Generic.List<FlatAction>();
        private int selected;
        internal SectionTabs()
        {
            BackColor = Palette.Background; Controls.Add(body); Controls.Add(headings);
        }
        internal void AddSection(string title, Control page)
        {
            int index = pages.Count; pages.Add(page); page.Dock = DockStyle.Fill; body.Controls.Add(page);
            var button = new FlatAction(title, false) { Selected = index == selected, Width = 156, Height = 36, Margin = new Padding(0, 0, 8, 0), AccessibleName = title };
            button.Click += delegate { SelectedIndex = index; };
            buttons.Add(button); headings.Controls.Add(button); SelectedIndex = selected;
        }
        internal int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= pages.Count) throw new ArgumentOutOfRangeException("value");
                selected = value;
                for (int i = 0; i < pages.Count; i++)
                {
                    pages[i].Visible = i == selected; buttons[i].Selected = i == selected;
                    buttons[i].AccessibleDescription = i == selected ? "当前页面" : "切换到此页";
                    buttons[i].Invalidate();
                }
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (pages.Count > 0 && (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab)))
            {
                SelectedIndex = (selected + (keyData == (Keys.Control | Keys.Tab) ? 1 : pages.Count - 1)) % pages.Count;
                buttons[selected].Focus(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
