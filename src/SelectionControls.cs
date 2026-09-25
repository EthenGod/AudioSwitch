// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
    // The native combo still handles selection, binding, keyboard navigation and accessibility.
    // Only the closed field and menu rows are drawn here.
    internal sealed class ChoiceBox : ComboBox
    {
        private bool hover;
        private bool menuOpen;
        private int committedIndex = -1;
        private readonly PopupRepaintWindow popup = new PopupRepaintWindow();
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ComboRect { internal int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ComboInfo { internal int Size; internal ComboRect Item, Button; internal int State; internal IntPtr Combo, Edit, List; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr combo, ref ComboInfo info);
        internal ChoiceBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat; BackColor = Palette.Card; ForeColor = Palette.Text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            IntegralHeight = false; MaxDropDownItems = 8; DropDownHeight = 240;
            ItemHeight = Font.Height + 8;
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 8; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Palette.NativeChrome(this); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnDropDown(EventArgs e)
        {
            committedIndex = SelectedIndex; menuOpen = true;
            var info = new ComboInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ComboInfo)) };
            if (GetComboBoxInfo(Handle, ref info) && info.List != IntPtr.Zero)
            { popup.ReleaseHandle(); popup.AssignHandle(info.List); PopupChrome.RemoveNativeFrame(info.List); popup.Round(); }
            Invalidate(); base.OnDropDown(e);
        }
        protected override void OnDropDownClosed(EventArgs e) { popup.ReleaseHandle(); menuOpen = false; Invalidate(); base.OnDropDownClosed(e); }
        protected override void OnSelectionChangeCommitted(EventArgs e) { committedIndex = SelectedIndex; base.OnSelectionChangeCommitted(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            // Paint a complete row off-screen; the native list copies it in one step.
            // Mouse movement must not invalidate/repaint the entire popup a second time.
            using (var buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, e.Bounds))
            {
                Size popupSize = menuOpen && popup.Handle != IntPtr.Zero ? PopupChrome.WindowSize(popup.Handle) : Size.Empty;
                PaintRow(buffer.Graphics, e, popupSize);
                // Native hover updates can draw just one row without WM_PAINT. Include
                // that row's part of the outline in the same buffer so it cannot be erased.
                if (!popupSize.IsEmpty) PopupChrome.DrawBorder(buffer.Graphics, popupSize);
                buffer.Render(e.Graphics);
            }
        }
        private void PaintRow(Graphics graphics, DrawItemEventArgs e, Size popupSize)
        {
            bool hot = (e.State & DrawItemState.Selected) != 0;
            // Native menus can preview another index while the pointer or keyboard moves.
            // The check belongs to the committed value, never to that temporary highlight.
            bool chosen = e.Index == (menuOpen ? committedIndex : SelectedIndex);
            bool field = (e.State & DrawItemState.ComboBoxEdit) != 0;
            float scale = graphics.DpiX / 96F;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Palette.Card)) graphics.FillRectangle(brush, e.Bounds);
            int side = (int)Math.Ceiling(6 * scale), top = (int)Math.Ceiling(2 * scale), bottom = top;
            // Fuller rows, with room for the popup's outer rim on the first/last row.
            if (!popupSize.IsEmpty) {
                top = Math.Max(top, 4 - e.Bounds.Top);
                bottom = Math.Max(bottom, e.Bounds.Bottom - (popupSize.Height - 4));
            }
            var bounds = new Rectangle(e.Bounds.X + side, e.Bounds.Y + top, Math.Max(1, e.Bounds.Width - side * 2), Math.Max(1, e.Bounds.Height - top - bottom));
            if (!field && (hot || chosen))
                using (var path = Palette.Round(bounds, 7 * scale))
                using (var brush = new SolidBrush(hot ? Palette.Hover : Palette.Selected)) graphics.FillPath(brush, path);
            TextRenderer.DrawText(graphics, GetItemText(Items[e.Index]), Font,
                new Rectangle(bounds.X + 8, e.Bounds.Y, Math.Max(1, bounds.Width - 34), e.Bounds.Height), Enabled ? Palette.Text : Palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
            if (!field && chosen) DrawCheck(graphics, bounds.Right - 21, e.Bounds.Top + e.Bounds.Height / 2, Palette.Accent);
        }
        internal static void DrawCheck(Graphics graphics, float x, float y, Color color)
        {
            using (var pen = new Pen(color, 1.8F))
            { pen.StartCap = pen.EndCap = LineCap.Round; graphics.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + 3, y + 3), new PointF(x + 9, y - 4) }); }
        }
        private void DrawField(Graphics graphics)
        {
            if (Width < 8 || Height < 8) return;
            float scale = graphics.DpiX / 96F;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var surface = Parent as Surface;
            Color background = surface != null ? surface.Fill : Parent == null ? Palette.Background : Parent.BackColor;
            using (var brush = new SolidBrush(background)) graphics.FillRectangle(brush, ClientRectangle);
            using (var path = Palette.Round(new RectangleF(1, 1, Width - 2, Height - 2), 7 * scale))
            using (var brush = new SolidBrush(!Enabled ? Palette.Disabled : hover || DroppedDown ? Palette.Hover : Palette.Card))
            using (var pen = new Pen(Enabled && (Focused || DroppedDown) ? Palette.Accent : hover && Enabled ? Palette.SelectedBorder : Palette.Border, Focused && Enabled ? 2 * scale : 1))
            { graphics.FillPath(brush, path); graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(graphics, SelectedItem == null ? Text : GetItemText(SelectedItem), Font,
                new Rectangle((int)(10 * scale), 0, Math.Max(1, Width - (int)(40 * scale)), Height), Enabled ? Palette.Text : Palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            float x = Width - 19 * scale, y = Height / 2F;
            using (var pen = new Pen(Enabled && DroppedDown ? Palette.Accent : Palette.Muted, 1.6F * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float direction = DroppedDown ? -1 : 1;
                graphics.DrawLines(pen, new[] { new PointF(x - 4 * scale, y - direction * 2 * scale), new PointF(x, y + direction * 2 * scale), new PointF(x + 4 * scale, y - direction * 2 * scale) });
            }
        }
        protected override void OnPaint(PaintEventArgs e) { DrawField(e.Graphics); }
        protected override void Dispose(bool disposing) { if (disposing) popup.ReleaseHandle(); base.Dispose(disposing); }
        private sealed class PopupRepaintWindow : NativeWindow
        {
            internal void Round() { if (Handle != IntPtr.Zero) PopupChrome.Round(Handle); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0085 && Handle != IntPtr.Zero) { PopupChrome.Border(Handle); m.Result = IntPtr.Zero; return; }
                base.WndProc(ref m);
                if (Handle == IntPtr.Zero) return;
                if (m.Msg == 0x0047) Round(); // Final popup bounds are known after positioning.
                if (m.Msg == 0x0085 || m.Msg == 0x000F) PopupChrome.Border(Handle);
                if (m.Msg == 0x0317) PopupChrome.Border(Handle, m.WParam);
            }
        }
    }

    internal sealed class PriorityList : ListBox
    {
        internal Func<int, bool> IsOnline;
        private int hovered = -1;
        private readonly ToolTip tips = new ToolTip();
        internal PriorityList()
        {
            DrawMode = DrawMode.OwnerDrawFixed; BorderStyle = BorderStyle.None;
            BackColor = Palette.Card; ForeColor = Palette.Text; IntegralHeight = false;
            ItemHeight = 50;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); Palette.NativeChrome(this);
            using (var g = CreateGraphics()) ItemHeight = (int)(50 * g.DpiY / 96F);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int next = IndexFromPoint(e.Location);
            if (next != hovered)
            {
                hovered = next; Invalidate(); tips.SetToolTip(this, next < 0 ? null : GetItemText(Items[next]));
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hovered = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool online = IsOnline == null || IsOnline(e.Index);
            float s = e.Graphics.DpiX / 96F;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Palette.Card)) e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
            using (var path = Palette.Round(bounds, 8 * s))
            using (var brush = new SolidBrush(selected ? Palette.Selected : hovered == e.Index ? Palette.Hover : Palette.Card))
            {
                e.Graphics.FillPath(brush, path);
                if (selected) using (var pen = new Pen(Focused ? Palette.Accent : Palette.SelectedBorder)) e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, (e.Index + 1).ToString(), Font, new Rectangle(bounds.X + 4, bounds.Y, (int)(33 * s), bounds.Height),
                selected ? Palette.Accent : Palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int left = bounds.X + (int)(44 * s), width = Math.Max(1, bounds.Width - (int)(75 * s));
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(left, bounds.Y + (int)(3 * s), width, (int)(23 * s)),
                online ? Palette.Text : Palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var font = new Font(Font.FontFamily, 8F)) TextRenderer.DrawText(e.Graphics, online ? "在线 · 可自动选择" : "离线 · 保留排序", font,
                new Rectangle(left, bounds.Y + (int)(25 * s), width, (int)(18 * s)), Palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (selected) ChoiceBox.DrawCheck(e.Graphics, bounds.Right - 21 * s, bounds.Top + bounds.Height / 2, Palette.Accent);
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
