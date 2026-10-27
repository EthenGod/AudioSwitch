// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal enum TrayGlyph { Panel, Browser, Output, Microphone, Devices, Game, Exit }
    internal class TrayActionItem : ToolStripMenuItem
    {
        internal readonly TrayGlyph Glyph;
        internal TrayActionItem(string text, TrayGlyph glyph) : base(text) { Glyph = glyph; }
    }
    internal sealed class TrayToggleItem : TrayActionItem
    {
        internal TrayToggleItem(string text, TrayGlyph glyph) : base(text, glyph) { }
    }
    // Radix Slate surfaces/text and Indigo interaction colors match the React panel.
    internal static class TrayColors
    {
        private static Color Pick(string light, string dark) { return ColorTranslator.FromHtml(Palette.Dark ? dark : light); }
        internal static Color Card { get { return Pick("#f9f9fb", "#18191b"); } }
        internal static Color Text { get { return Pick("#1c2024", "#edeef0"); } }
        internal static Color Muted { get { return Pick("#60646c", "#b0b4ba"); } }
        internal static Color Border { get { return Pick("#d9d9e0", "#363a3f"); } }
        internal static Color Hover { get { return Pick("#f0f0f3", "#212225"); } }
        internal static Color Pressed { get { return Pick("#e8e8ec", "#272a2d"); } }
        internal static Color Accent { get { return Pick("#3e63dd", "#3e63dd"); } }
        internal static Color Selected { get { return Pick("#edf2fe", "#182449"); } }
        internal static Color AccentText { get { return Pick("#3a5bc7", "#9eb1ff"); } }
    }
    internal sealed class TrayMenuHeading : ToolStripMenuItem { internal TrayMenuHeading() : base("声间") { Enabled = false; } }
    internal sealed class TrayDeviceGroup : ToolStripMenuItem
    {
        internal readonly string DeviceName;
        internal readonly string Title;
        internal TrayDeviceGroup(string title, string device) : base(title)
        { Title = title; DeviceName = device; ToolTipText = title + "：" + device; }
    }
    internal sealed class ThemedMenu : ContextMenuStrip
    {
        private readonly Font menuFont = new Font("Microsoft YaHei UI", 10.5F);
        internal ThemedMenu()
        {
            Font = menuFont; Renderer = new MenuRenderer();
            ShowImageMargin = false; ShowCheckMargin = true;
            AutoSize = false;
            Padding = new Padding(7); BackColor = TrayColors.Card; ForeColor = TrayColors.Text;
            ShowItemToolTips = true; DropShadowEnabled = true;
        }
        protected override void OnItemAdded(ToolStripItemEventArgs e)
        {
            base.OnItemAdded(e);
            if (!(e.Item is ToolStripSeparator)) {
                if (String.IsNullOrEmpty(e.Item.ToolTipText)) e.Item.ToolTipText = e.Item.Text;
            }
        }
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            BackColor = TrayColors.Card; ForeColor = TrayColors.Text;
            base.OnOpening(e);
            // Use one measured width for the popup, every hit target and every paint area.
            // The native menu layout otherwise clips padded rows and capped long names.
            SuspendLayout();
            try {
                using (var graphics = CreateGraphics()) {
                    float scale = Math.Max(graphics.DpiX / 96F, Font.SizeInPoints / 10.5F);
                    int width = (int)Math.Ceiling(280 * scale);
                    int totalHeight = 0;
                    foreach (ToolStripItem item in Items) {
                        var group = item as TrayDeviceGroup;
                        int text = TextRenderer.MeasureText(graphics, item.Text, item.Font).Width;
                        if (group != null) text = Math.Max(text, TextRenderer.MeasureText(graphics, group.DeviceName, item.Font).Width);
                        width = Math.Max(width, text + (int)Math.Ceiling(88 * scale));
                    }
                    width = Math.Min(width, (int)Math.Ceiling(380 * scale));
                    Padding = new Padding((int)Math.Ceiling(6 * scale));
                    foreach (ToolStripItem item in Items) {
                        int line = (int)Math.Ceiling(item.Font.GetHeight(graphics));
                        int height = item is ToolStripSeparator ? (int)Math.Ceiling(9 * scale)
                            : item is TrayDeviceGroup ? 2 * line + (int)Math.Ceiling(21 * scale)
                            : line + (int)Math.Ceiling(18 * scale);
                        item.AutoSize = false; item.Margin = Padding.Empty; item.Padding = Padding.Empty;
                        item.Size = new Size(width, height);
                        if (item.Available) totalHeight += height;
                    }
                    Size = new Size(width + 1, totalHeight + 4);
                }
            } finally { ResumeLayout(true); }
            PerformLayout();
        }
        protected override void OnOpened(EventArgs e) { base.OnOpened(e); PopupChrome.Round(Handle); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (IsHandleCreated) PopupChrome.Round(Handle); }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) menuFont.Dispose(); }

        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            internal MenuRenderer() { RoundedEdges = false; }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(TrayColors.Card); }
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip.Width < 3 || e.ToolStrip.Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Palette.Round(new RectangleF(.5F, .5F, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8 * e.Graphics.DpiX / 96F))
                using (var pen = new Pen(TrayColors.Border)) e.Graphics.DrawPath(pen, path);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var menuItem = e.Item as ToolStripMenuItem;
                bool current = menuItem != null && menuItem.Checked && !(menuItem is TrayToggleItem);
                if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed && !current)) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Palette.Round(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6 * e.Graphics.DpiX / 96F))
                using (var brush = new SolidBrush(e.Item.Pressed ? TrayColors.Pressed : e.Item.Selected ? TrayColors.Hover : TrayColors.Selected)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var group = e.Item as TrayDeviceGroup;
                float scale = Math.Max(e.Graphics.DpiX / 96F, e.TextFont.SizeInPoints / 10.5F);
                int left = (int)Math.Ceiling(38 * scale), right = (int)Math.Ceiling((e.Item is TrayToggleItem ? 54 : 28) * scale);
                var textBounds = new Rectangle(left, 0, Math.Max(1, e.Item.Width - left - right), e.Item.Height);
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (group != null) {
                    DrawGlyph(e.Graphics, group.Title == "声音输出" ? TrayGlyph.Output : TrayGlyph.Microphone, scale, e.Item.Height, TrayColors.Muted);
                    using (var font = new Font(e.TextFont, FontStyle.Bold)) {
                        int line = (int)Math.Ceiling(Math.Max(font.GetHeight(e.Graphics), e.TextFont.GetHeight(e.Graphics)));
                        int gap = (int)Math.Ceiling(3 * scale), top = (e.Item.Height - line * 2 - gap) / 2;
                        TextRenderer.DrawText(e.Graphics, group.Title, font, new Rectangle(left, top, textBounds.Width, line), TrayColors.Text, flags);
                        TextRenderer.DrawText(e.Graphics, group.DeviceName, e.TextFont,
                            new Rectangle(left, top + line + gap, textBounds.Width, line), TrayColors.Muted, flags);
                    }
                    return;
                }
                if (e.Item is TrayMenuHeading) {
                    using (var pen = new Pen(TrayColors.Accent, 2 * scale)) {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        for (int i = 0; i < 4; i++) { float x = (13 + i * 4) * scale, h = (i == 1 ? 14 : i == 3 ? 10 : 6) * scale; e.Graphics.DrawLine(pen, x, (e.Item.Height - h) / 2, x, (e.Item.Height + h) / 2); }
                    }
                    using (var font = new Font(e.TextFont, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "声间", font, textBounds, TrayColors.Text, flags);
                    return;
                }
                var action = e.Item as TrayActionItem;
                if (action != null) DrawGlyph(e.Graphics, action.Glyph, scale, e.Item.Height, e.Item.Enabled ? TrayColors.Text : TrayColors.Muted);
                var toggle = e.Item as TrayToggleItem;
                if (toggle != null) DrawToggle(e.Graphics, toggle, scale);
                TextRenderer.DrawText(e.Graphics, e.Item.Text, e.TextFont, textBounds,
                    !e.Item.Enabled || e.Item is ToolStripLabel ? TrayColors.Muted : TrayColors.Text, flags);
            }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                if (e.Item is TrayToggleItem) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Max(e.Graphics.DpiX / 96F, e.Item.Font.SizeInPoints / 9F);
                float x = 12 * scale, y = e.Item.Height / 2F;
                using (var pen = new Pen(TrayColors.AccentText, 1.7F * scale)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + 3 * scale, y + 3 * scale), new PointF(x + 9 * scale, y - 4 * scale) });
                }
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                float scale = Math.Max(e.Graphics.DpiX / 96F, e.Item.Font.SizeInPoints / 9F);
                float x = e.Item.Width - 15 * scale, y = e.Item.Height / 2F;
                float direction = e.Direction == ArrowDirection.Left ? -1 : 1;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(e.Item.Enabled ? TrayColors.Muted : TrayColors.Border, 1.5F * scale)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - direction * 2 * scale, y - 3 * scale), new PointF(x + direction * 1 * scale, y), new PointF(x - direction * 2 * scale, y + 3 * scale) });
                }
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            { using (var pen = new Pen(TrayColors.Border)) e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2); }
            private static void DrawToggle(Graphics graphics, TrayToggleItem item, float scale)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var track = new RectangleF(item.Width - 44 * scale, item.Height / 2F - 9 * scale, 32 * scale, 18 * scale);
                using (var path = Palette.Round(track, 9 * scale))
                using (var brush = new SolidBrush(item.Checked ? TrayColors.Accent : TrayColors.Border)) graphics.FillPath(brush, path);
                using (var brush = new SolidBrush(item.Checked ? Color.White : TrayColors.Muted))
                    graphics.FillEllipse(brush, track.X + (item.Checked ? 16 : 2) * scale, track.Y + 2 * scale, 14 * scale, 14 * scale);
            }
            private static void DrawGlyph(Graphics graphics, TrayGlyph glyph, float scale, int height, Color color)
            {
                var saved = graphics.Save();
                try {
                    graphics.TranslateTransform(12 * scale, height / 2F - 8 * scale); graphics.ScaleTransform(scale, scale);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var pen = new Pen(color, 1.4F)) {
                        pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                        switch (glyph) {
                            case TrayGlyph.Panel: graphics.DrawRectangle(pen, 1, 2, 15, 12); graphics.DrawLine(pen, 6, 2, 6, 14); break;
                            case TrayGlyph.Browser: graphics.DrawEllipse(pen, 1, 1, 15, 15); graphics.DrawEllipse(pen, 5, 1, 7, 15); graphics.DrawLine(pen, 1, 8.5F, 16, 8.5F); break;
                            case TrayGlyph.Output: graphics.DrawArc(pen, 2, 2, 13, 13, 180, 180); graphics.DrawRectangle(pen, 2, 9, 3, 6); graphics.DrawRectangle(pen, 12, 9, 3, 6); break;
                            case TrayGlyph.Microphone: using (var path = Palette.Round(new RectangleF(5, 1, 6, 10), 3)) graphics.DrawPath(pen, path); graphics.DrawArc(pen, 2, 4, 12, 10, 0, 180); graphics.DrawLine(pen, 8, 14, 8, 17); break;
                            case TrayGlyph.Devices: graphics.DrawLines(pen, new[] { new PointF(2,5), new PointF(15,5), new PointF(12,2) }); graphics.DrawLines(pen, new[] { new PointF(15,12), new PointF(2,12), new PointF(5,15) }); break;
                            case TrayGlyph.Game: using (var path = Palette.Round(new RectangleF(1, 4, 15, 10), 4)) graphics.DrawPath(pen, path); graphics.DrawLine(pen, 4, 9, 8, 9); graphics.DrawLine(pen, 6, 7, 6, 11); graphics.DrawEllipse(pen, 12, 7, 1, 1); break;
                            case TrayGlyph.Exit: graphics.DrawArc(pen, 2, 2, 13, 13, -55, 290); graphics.DrawLine(pen, 8.5F, 0, 8.5F, 8); break;
                        }
                    }
                } finally { graphics.Restore(saved); }
            }
        }
    }
}
