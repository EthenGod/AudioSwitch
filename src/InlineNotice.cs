using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class InlineNotice : Panel
    {
        private readonly Surface card = new Surface();
        private readonly Label heading = Palette.Label("", 9.5F, Palette.Text, true);
        private readonly NoticeMark mark = new NoticeMark();
        internal readonly Label MessageLabel = Palette.Label("", 8.5F, Palette.Muted, false);
        internal readonly FlatAction CloseButton = new FlatAction("×", false) { Name = "dismissNotice", AccessibleName = "关闭此提示" };
        internal readonly FlatAction DetailsButton = new FlatAction("查看详情", false) { Name = "noticeDetails" };
        private bool failure;
        internal InlineNotice()
        {
            Name = "panelNotice"; Height = 104; BackColor = Palette.Background;
            MessageLabel.Name = "noticeText"; MessageLabel.UseMnemonic = false;
            heading.Name = "noticeHeading";
            card.Controls.Add(mark); card.Controls.Add(heading); card.Controls.Add(MessageLabel);
            card.Controls.Add(DetailsButton); card.Controls.Add(CloseButton); Controls.Add(card);
            DetailsButton.Click += delegate { NoticeDialog.ShowNotice(FindForm(), heading.Text, MessageLabel.Text, failure); };
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        internal void SetMessage(string message, bool isFailure)
        {
            failure = isFailure; mark.Failure = isFailure;
            heading.Text = isFailure ? "操作未完成" : "需要留意";
            MessageLabel.Text = message ?? ""; mark.Invalidate();
            Visible = MessageLabel.Text.Length > 0;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (card == null) return;
            float s = 1F; if (IsHandleCreated) using (var graphics = CreateGraphics()) s = graphics.DpiX / 96F;
            card.SetBounds(0, (int)(4 * s), Math.Max(1, Width), Height - (int)(12 * s));
            mark.SetBounds((int)(16 * s), (int)(17 * s), (int)(28 * s), (int)(28 * s));
            int textLeft = (int)(56 * s), actionLeft = card.Width - (int)(152 * s);
            heading.SetBounds(textLeft, (int)(13 * s), Math.Max(1, actionLeft - textLeft - (int)(12 * s)), (int)(23 * s));
            MessageLabel.SetBounds(textLeft, (int)(39 * s), heading.Width, (int)(38 * s));
            DetailsButton.SetBounds(actionLeft, (int)(28 * s), (int)(96 * s), (int)(32 * s));
            CloseButton.SetBounds(card.Width - (int)(44 * s), (int)(28 * s), (int)(30 * s), (int)(32 * s));
        }
        private sealed class NoticeMark : Control
        {
            internal bool Failure;
            internal NoticeMark() { DoubleBuffered = true; BackColor = Palette.Card; }
            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Palette.ParentBackground(this)); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Palette.ParentBackground(this));
                Color ink = Failure ? (Palette.Dark ? Color.FromArgb(255, 154, 151) : Color.FromArgb(184, 57, 66)) : Palette.Warning;
                Color fill = Failure ? (Palette.Dark ? Color.FromArgb(64, 37, 43) : Color.FromArgb(255, 237, 238)) : Palette.WarningBackground;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(fill)) e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
                using (var font = new Font("Segoe UI", 12, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "!", font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
