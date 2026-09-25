using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class TickOption : CheckBox
    {
        private bool hover;
        internal TickOption()
        {
            Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Palette.ParentBackground(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Palette.ParentBackground(this));
            float s = e.Graphics.DpiX / 96F; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(2 * s, (Height - 17 * s) / 2, 17 * s, 17 * s);
            using (var path = Palette.Round(box, 4 * s))
            using (var fill = new SolidBrush(!Enabled ? Palette.Disabled : Checked ? Palette.Primary : hover ? Palette.Hover : Palette.Card))
            using (var pen = new Pen(Enabled && (Checked || hover || Focused) ? Palette.Accent : Palette.Border, Focused && ShowFocusCues ? 2 * s : s))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
            if (Checked) ChoiceBox.DrawCheck(e.Graphics, box.Left + 4 * s, box.Top + 8 * s, Enabled ? Color.White : Palette.Muted);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(27 * s), 0, Width - (int)(27 * s), Height), Enabled ? ForeColor : Palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // The native numeric editor is clipped to its text field. The wrapper owns all
    // visible borders/buttons, avoiding UpDownBase's fixed-height painting over them.
    internal sealed class NumberBox : UserControl
    {
        private readonly Panel field = new Panel();
        private readonly QuietNumber editor = new QuietNumber { BorderStyle = BorderStyle.None };
        private bool arranging;
        private bool buttons = true;
        internal bool ShowButtons { get { return buttons; } set { buttons = value; PerformLayout(); Invalidate(); } }
        internal decimal Minimum { get { return editor.Minimum; } set { editor.Minimum = value; } }
        internal decimal Maximum { get { return editor.Maximum; } set { editor.Maximum = value; } }
        internal decimal Increment { get { return editor.Increment; } set { editor.Increment = value; } }
        internal int DecimalPlaces { get { return editor.DecimalPlaces; } set { editor.DecimalPlaces = value; } }
        internal HorizontalAlignment TextAlign { get { return editor.TextAlign; } set { editor.TextAlign = value; } }
        internal decimal Value { get { return editor.Value; } set { editor.Value = value; } }
        public override string Text { get { return editor == null ? base.Text : editor.Text; } set { if (editor == null) base.Text = value; else editor.Text = value; } }
        internal event EventHandler ValueChanged;
        internal NumberBox()
        {
            AutoScaleMode = AutoScaleMode.None; BackColor = Palette.Card; ForeColor = Palette.Text; Height = 30;
            TabStop = false; AccessibleRole = AccessibleRole.SpinButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            field.Controls.Add(editor); Controls.Add(field);
            editor.ValueChanged += delegate { if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); };
            editor.Enter += delegate { Invalidate(); }; editor.Leave += delegate { Invalidate(); };
            editor.BackColor = BackColor; editor.ForeColor = ForeColor;
        }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); if (editor != null) editor.BackColor = BackColor; }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); if (editor != null) editor.ForeColor = ForeColor; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (arranging || editor == null || field == null) return;
            arranging = true;
            try {
                // Measuring a not-yet-parented control must not create its native window.
                // The form owns the initial DPI scaling after its entire tree is assembled.
                float s = 1F; if (IsHandleCreated) using (var g = CreateGraphics()) s = g.DpiX / 96F;
                int gap = (int)(5 * s), arrow = buttons ? (int)(20 * s) : 0;
                int height = editor.PreferredHeight;
                field.SetBounds(gap, Math.Max(2, (Height - height) / 2), Math.Max(8, Width - gap * 2 - arrow), height);
                editor.SetBounds(0, 0, field.Width + SystemInformation.VerticalScrollBarWidth + 2, height);
                editor.AccessibleName = AccessibleName;
            } finally { arranging = false; }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            float s = e.Graphics.DpiX / 96F; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Palette.Round(new RectangleF(1, 1, Width - 2, Height - 2), 6 * s))
            using (var pen = new Pen(Enabled && ContainsFocus ? Palette.Accent : Palette.Border, Enabled && ContainsFocus ? 2 * s : 1)) e.Graphics.DrawPath(pen, path);
            if (!buttons) return;
            float x = Width - 12 * s;
            using (var pen = new Pen(Enabled ? Palette.Muted : Palette.Border, 1.4F * s))
            {
                e.Graphics.DrawLines(pen, new[] { new PointF(x - 3 * s, Height * .28F + 2 * s), new PointF(x, Height * .28F - s), new PointF(x + 3 * s, Height * .28F + 2 * s) });
                e.Graphics.DrawLines(pen, new[] { new PointF(x - 3 * s, Height * .72F - 2 * s), new PointF(x, Height * .72F + s), new PointF(x + 3 * s, Height * .72F - 2 * s) });
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left) return;
            editor.Focus();
            if (buttons && e.X >= field.Right) { if (e.Y < Height / 2) editor.UpButton(); else editor.DownButton(); }
            Invalidate();
        }
        private sealed class QuietNumber : NumericUpDown
        {
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (ContainsFocus) { base.OnMouseWheel(e); return; }
                for (Control parent = Parent; parent != null; parent = parent.Parent)
                {
                    var canvas = parent as ScrollCanvas;
                    if (canvas != null) { canvas.ScrollBy(-e.Delta / 120 * 48); return; }
                }
            }
        }
    }
    internal sealed class LevelSlider : Control
    {
        internal int Minimum = 0;
        internal int Maximum = 100;
        internal int SmallChange = 1;
        internal bool Vertical;
        internal decimal DisplayDivisor = 1;
        private int value;
        private bool dragging;
        private float grabOffset;
        private int lastPointer;
        private bool hover;
        internal event EventHandler ValueChanged;
        internal int Value
        {
            get { return value; }
            set {
                int next = Math.Max(Minimum, Math.Min(Maximum, value));
                if (this.value == next) return;
                this.value = next; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        internal LevelSlider()
        {
            TabStop = true; Cursor = Cursors.Hand; Size = new Size(200, 32);
            AccessibleRole = AccessibleRole.Slider;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        private float ScaleFactor { get { using (var g = CreateGraphics()) return g.DpiX / 96F; } }
        private float Position(int number, float s)
        {
            float inset = 11 * s, length = Math.Max(1, (Vertical ? Height : Width) - 2 * inset);
            float fraction = Maximum == Minimum ? 0 : (number - Minimum) / (float)(Maximum - Minimum);
            return inset + (Vertical ? 1 - fraction : fraction) * length;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float s = e.Graphics.DpiX / 96F; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float lo = Position(Minimum, s), hi = Position(Maximum, s), current = Position(Value, s), zero = Position(Math.Max(Minimum, 0), s);
            float center = (Vertical ? Width : Height) / 2F;
            using (var track = new Pen(Palette.Border, 4 * s))
            using (var active = new Pen(Enabled ? Palette.Accent : Palette.SwitchOff, 4 * s))
            {
                track.StartCap = track.EndCap = active.StartCap = active.EndCap = LineCap.Round;
                if (Vertical) { e.Graphics.DrawLine(track, center, lo, center, hi); e.Graphics.DrawLine(active, center, zero, center, current); }
                else { e.Graphics.DrawLine(track, lo, center, hi, center); e.Graphics.DrawLine(active, zero, center, current, center); }
            }
            if (Minimum < 0 && Maximum > 0) using (var pen = new Pen(Palette.Muted))
            { if (Vertical) e.Graphics.DrawLine(pen, center - 7 * s, zero, center + 7 * s, zero); }
            float x = Vertical ? center : current, y = Vertical ? current : center;
            if (Enabled && (hover || dragging || Focused)) using (var brush = new SolidBrush(Palette.Selected)) e.Graphics.FillEllipse(brush, x - 11 * s, y - 11 * s, 22 * s, 22 * s);
            using (var fill = new SolidBrush(Enabled ? Palette.Card : Palette.Disabled))
            using (var pen = new Pen(Enabled ? Palette.Accent : Palette.SwitchOff, 2 * s))
            { e.Graphics.FillEllipse(fill, x - 7 * s, y - 7 * s, 14 * s, 14 * s); e.Graphics.DrawEllipse(pen, x - 7 * s, y - 7 * s, 14 * s, 14 * s); }
        }
        private void SetFromPointer(MouseEventArgs e)
        {
            float s = ScaleFactor, inset = 11 * s, length = Math.Max(1, (Vertical ? Height : Width) - 2 * inset);
            float fraction = ((Vertical ? e.Y : e.X) - grabOffset - inset) / length;
            if (Vertical) fraction = 1 - fraction;
            int step = Math.Max(1, SmallChange);
            Value = Minimum + (int)Math.Round(Math.Max(0, Math.Min(1, fraction)) * (Maximum - Minimum) / step) * step;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus(); float s = ScaleFactor, pointer = Vertical ? e.Y : e.X;
            float distance = pointer - Position(Value, s);
            bool thumb = Math.Abs(distance) <= 10 * s;
            grabOffset = thumb ? distance : 0; lastPointer = Vertical ? e.Y : e.X; dragging = true; Capture = true;
            if (!thumb) SetFromPointer(e); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int pointer = Vertical ? e.Y : e.X;
            if (dragging && Enabled && pointer != lastPointer) { lastPointer = pointer; SetFromPointer(e); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; Invalidate(); } base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { dragging = false; Capture = false; } Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData)
        { var key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) Value += SmallChange;
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) Value -= SmallChange;
            else if (e.KeyCode == Keys.PageUp) Value += SmallChange * 10;
            else if (e.KeyCode == Keys.PageDown) Value -= SmallChange * 10;
            else if (e.KeyCode == Keys.Home) Value = Minimum;
            else if (e.KeyCode == Keys.End) Value = Maximum;
            else { base.OnKeyDown(e); return; }
            e.Handled = e.SuppressKeyPress = true;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessible(this); }
        private sealed class SliderAccessible : ControlAccessibleObject
        {
            private readonly LevelSlider slider;
            internal SliderAccessible(LevelSlider slider) : base(slider) { this.slider = slider; }
            public override string Value { get { return (slider.Value / slider.DisplayDivisor).ToString(CultureInfo.CurrentCulture); } set { decimal number; if (slider.Enabled && Decimal.TryParse(value, out number)) slider.Value = (int)Math.Max(slider.Minimum, Math.Min(slider.Maximum, number * slider.DisplayDivisor)); } }
        }
    }
}
