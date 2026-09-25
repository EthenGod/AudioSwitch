using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class NoticeDialog : Form
    {
        internal NoticeDialog(string title, string message, bool error, bool confirm)
        {
            SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F); BackColor = Palette.Background; ForeColor = Palette.Text;
            Text = title + " · 声间"; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var glyph = new NoticeGlyph(error); glyph.SetBounds(24, 24, 42, 42); Controls.Add(glyph);
            var heading = Palette.Label(title, 14, Palette.Text, true); heading.SetBounds(80, 23, 446, 34); Controls.Add(heading);
            var hint = Palette.Label(error ? "查看下方原因，处理后可重试。" : confirm ? "请确认以下变更。" : "操作结果", 9, Palette.Muted, false);
            hint.SetBounds(80, 60, 446, 26); Controls.Add(hint);
            var card = new Surface(); card.SetBounds(24, 104, 512, 220); Controls.Add(card);
            var body = new NoticeBody(message ?? ""); body.Dock = DockStyle.Fill; card.Padding = new Padding(14); card.Controls.Add(body);
            int bodyHeight = Math.Max(92, Math.Min(280, body.MessageHeight + 8));
            card.Height = bodyHeight + 28;
            int footer = card.Bottom + 24; ClientSize = new Size(560, footer + 60);
            var status = Palette.Label("", 8, Palette.Muted, false); status.SetBounds(24, footer - 20, 250, 20); Controls.Add(status);
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
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(error ? Palette.WarningBackground : Palette.Selected)) e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
                using (var font = new Font("Segoe UI", 19, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, error ? "!" : "i", font, ClientRectangle,
                    error ? Palette.Warning : Palette.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
