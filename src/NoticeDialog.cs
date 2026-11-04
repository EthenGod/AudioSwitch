// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class NoticeDialog : MessageForm
    {
        internal NoticeDialog(string title, string message, bool error, bool confirm, Func<bool> systemDark = null) : base(error, systemDark)
        {
            SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = "声间 " + title; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var glyph = new NoticeGlyph(error); glyph.SetBounds(24, 24, 24, 24); Controls.Add(glyph);
            var heading = Palette.Label(title, 15, Theme.Text, true); heading.SetBounds(60, 20, 474, 32); Controls.Add(heading);
            var card = new Surface(); card.SetBounds(24, 68, 512, 220); Controls.Add(card);
            var body = new NoticeBody(message ?? ""); body.Dock = DockStyle.Fill; card.Padding = new Padding(14); card.Controls.Add(body);
            int bodyHeight = Math.Max(28, Math.Min(280, body.MessageHeight + 8));
            card.Height = bodyHeight + 28;
            int footer = card.Bottom + 16; ClientSize = new Size(560, footer + 56);
            var status = Palette.Label("", 9, Theme.Muted, false); status.AccessibleRole = AccessibleRole.StatusBar;
            status.SetBounds(142, footer, confirm ? 152 : 270, 36); Controls.Add(status);
            var copy = new FlatAction("复制详情", false) { Name = "copyDetails" }; copy.SetBounds(24, footer, 106, 36); Controls.Add(copy);
            copy.Click += delegate {
                try { Clipboard.SetText(message ?? ""); status.Text = "详情已复制"; }
                catch { status.Text = "暂时无法复制，请重试。"; }
            };
            var accept = new FlatAction(confirm ? "确认导入" : "知道了", true) { Name = "noticeAccept", DialogResult = DialogResult.OK };
            accept.SetBounds(424, footer, 112, 36); Controls.Add(accept); AcceptButton = accept;
            if (confirm) {
                var cancel = new FlatAction("取消", false) { Name = "noticeCancel", DialogResult = DialogResult.Cancel };
                cancel.SetBounds(306, footer, 106, 36); Controls.Add(cancel); CancelButton = cancel;
                ActiveControl = cancel; // Enter must not confirm a replacement accidentally.
            } else { CancelButton = accept; ActiveControl = accept; }
            Shown += delegate { Palette.ChromeTree(this); };
            ResumeLayout(true);
        }
        internal static DialogResult ShowNotice(IWin32Window owner, string title, string message, bool error = false, bool confirm = false)
        {
            using (var dialog = new NoticeDialog(title, message, error, confirm)) {
                if (owner == null) { dialog.StartPosition = FormStartPosition.CenterScreen; dialog.ShowInTaskbar = true; }
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
        }
        private sealed class NoticeBody : ScrollCanvas
        {
            internal int MessageHeight;
            internal NoticeBody(string message)
            {
                BackColor = Palette.Card;
                string visible = message.Length > 24000 ? message.Substring(0, 24000) + "\n\n内容较长，请复制详情查看完整内容。" : message;
                var label = Palette.Label(visible, 9, Palette.Text, false); label.AutoEllipsis = false; label.UseMnemonic = false;
                MessageHeight = TextRenderer.MeasureText(visible, label.Font, new Size(454, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + 8;
                label.SetBounds(0, 0, 454, MessageHeight); Canvas.Controls.Add(label);
            }
        }
        private sealed class NoticeGlyph : Control
        {
            private readonly bool error;
            internal NoticeGlyph(bool error) { this.error = error; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var theme = MessageForm.ColorsOf(this);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.ScaleTransform(Width / 24F, Height / 24F);
                using (var pen = new Pen(error ? theme.Danger : theme.AccentText, 1.8F)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawEllipse(pen, 2, 2, 20, 20);
                    e.Graphics.DrawLine(pen, 12, error ? 7 : 11, 12, error ? 13 : 17);
                    using (var brush = new SolidBrush(pen.Color)) e.Graphics.FillEllipse(brush, 11, error ? 16 : 7, 2, 2);
                }
            }
        }
    }
}
