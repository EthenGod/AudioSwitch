// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
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
        private readonly Font menuFont = new Font("Microsoft YaHei UI", 9F);
        internal ThemedMenu()
        {
            Font = menuFont; Renderer = new MenuRenderer();
            ShowImageMargin = false; ShowCheckMargin = true;
            AutoSize = false;
            Padding = new Padding(7); BackColor = Palette.Card; ForeColor = Palette.Text;
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
            BackColor = Palette.Card; ForeColor = Palette.Text;
            base.OnOpening(e);
            // Use one measured width for the popup, every hit target and every paint area.
            // The native menu layout otherwise clips padded rows and capped long names.
            SuspendLayout();
            try {
                using (var graphics = CreateGraphics()) {
                    float scale = Math.Max(graphics.DpiX / 96F, Font.SizeInPoints / 9F);
                    int width = (int)Math.Ceiling(240 * scale);
                    int totalHeight = 0;
                    foreach (ToolStripItem item in Items) {
                        var group = item as TrayDeviceGroup;
                        int text = TextRenderer.MeasureText(graphics, item.Text, item.Font).Width;
                        if (group != null) text = Math.Max(text, TextRenderer.MeasureText(graphics, group.DeviceName, item.Font).Width);
                        width = Math.Max(width, text + (int)Math.Ceiling(64 * scale));
                    }
                    width = Math.Min(width, (int)Math.Ceiling(380 * scale));
                    Padding = new Padding((int)Math.Ceiling(6 * scale));
                    foreach (ToolStripItem item in Items) {
                        int line = (int)Math.Ceiling(item.Font.GetHeight(graphics));
                        int height = item is ToolStripSeparator ? (int)Math.Ceiling(9 * scale)
                            : item is TrayDeviceGroup ? 2 * line + (int)Math.Ceiling(21 * scale)
                            : line + (int)Math.Ceiling(16 * scale);
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
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Palette.Card); }
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip.Width < 3 || e.ToolStrip.Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Palette.Round(new RectangleF(.5F, .5F, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8 * e.Graphics.DpiX / 96F))
                using (var pen = new Pen(Palette.Border)) e.Graphics.DrawPath(pen, path);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Palette.Round(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6 * e.Graphics.DpiX / 96F))
                using (var brush = new SolidBrush(e.Item.Pressed ? Palette.Pressed : Palette.Hover)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var group = e.Item as TrayDeviceGroup;
                float scale = Math.Max(e.Graphics.DpiX / 96F, e.TextFont.SizeInPoints / 9F);
                int left = (int)Math.Ceiling(32 * scale), right = (int)Math.Ceiling(28 * scale);
                var textBounds = new Rectangle(left, 0, Math.Max(1, e.Item.Width - left - right), e.Item.Height);
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (group != null) {
                    using (var font = new Font(e.TextFont, FontStyle.Bold)) {
                        int line = (int)Math.Ceiling(Math.Max(font.GetHeight(e.Graphics), e.TextFont.GetHeight(e.Graphics)));
                        int gap = (int)Math.Ceiling(3 * scale), top = (e.Item.Height - line * 2 - gap) / 2;
                        TextRenderer.DrawText(e.Graphics, group.Title, font, new Rectangle(left, top, textBounds.Width, line), Palette.Text, flags);
                        TextRenderer.DrawText(e.Graphics, group.DeviceName, e.TextFont,
                            new Rectangle(left, top + line + gap, textBounds.Width, line), Palette.Muted, flags);
                    }
                    return;
                }
                if (e.Item is TrayMenuHeading) {
                    using (var font = new Font(e.TextFont, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "声间", font, textBounds, Palette.Accent, flags);
                    return;
                }
                TextRenderer.DrawText(e.Graphics, e.Item.Text, e.TextFont, textBounds,
                    !e.Item.Enabled || e.Item is ToolStripLabel ? Palette.Muted : Palette.Text, flags);
            }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Max(e.Graphics.DpiX / 96F, e.Item.Font.SizeInPoints / 9F);
                float x = 12 * scale, y = e.Item.Height / 2F;
                using (var pen = new Pen(Palette.Accent, 1.7F * scale)) {
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
                using (var pen = new Pen(e.Item.Enabled ? Palette.Muted : Palette.Border, 1.5F * scale)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - direction * 2 * scale, y - 3 * scale), new PointF(x + direction * 1 * scale, y), new PointF(x - direction * 2 * scale, y + 3 * scale) });
                }
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            { using (var pen = new Pen(Palette.Border)) e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2); }
        }
    }
}
