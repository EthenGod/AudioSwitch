// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    // Detached command snapshots. Audio work is always posted to the backend dispatcher.
    internal sealed class TrayCommand
    {
        internal string Text, Detail, Key;
        internal TrayGlyph Glyph;
        internal bool Enabled = true, Checked, Toggle, BreakBefore, KeepOpen, AnimateClose;
        internal Action Execute;
        internal IList<TrayCommand> Children;
        internal TrayCommand(string text, TrayGlyph glyph, Action execute = null) { Text = text; Glyph = glyph; Execute = execute; }
    }

    // A short-lived STA keeps painting off the audio/IPC dispatcher. No hidden window survives Close.
    internal sealed class TrayPopupHost : IDisposable
    {
        private readonly object sync = new object();
        private readonly Action<Action> post;
        private readonly Action<Exception> error;
        private readonly Func<IList<TrayCommand>> snapshot;
        private TrayPopup popup;
        private Thread thread;
        private bool disposed, commandPending;
        private Action pendingShow;
        private IList<TrayCommand> latestCommands;
        internal bool Running { get { lock (sync) return thread != null; } }
        internal TrayPopupHost(Action<Action> post, Action<Exception> error, Func<IList<TrayCommand>> snapshot = null) { this.post = post; this.error = error; this.snapshot = snapshot; }
        internal void Show(Point anchor, IList<TrayCommand> commands, bool dark)
        {
            lock (sync) {
                if (disposed) return;
                latestCommands = commands;
                if (thread != null) {
                    pendingShow = () => Show(anchor, commands, dark);
                    var existing = popup;
                    if (existing != null && existing.IsHandleCreated && !existing.IsDisposed) {
                        try { existing.BeginInvoke((Action)(() => { if (!existing.IsDisposed) { lock (sync) pendingShow = null; existing.UpdateSnapshot(commands); existing.Reopen(); } })); return; }
                        catch (InvalidOperationException) { }
                    }
                    return;
                }
                thread = new Thread(delegate() {
                    Action chosen = null;
                    try {
                        using (var window = new TrayPopup(commands, anchor, dark, dispatch: Dispatch)) {
                            window.HandleCreated += delegate { lock (sync) {
                                if (disposed) window.BeginInvoke((Action)(() => window.Close()));
                                else if (pendingShow != null) window.BeginInvoke((Action)(() => {
                                    IList<TrayCommand> next; lock (sync) { next = latestCommands; pendingShow = null; }
                                    if (!window.IsDisposed) { window.UpdateSnapshot(next); window.Reopen(); }
                                }));
                            } };
                            lock (sync) { popup = window; if (disposed) return; }
                            Application.Run(window);
                            chosen = window.Chosen;
                        }
                    } catch (Exception ex) { try { post(() => error(ex)); } catch (InvalidOperationException) { } }
                    finally {
                        lock (sync) {
                            popup = null; thread = null;
                            var next = pendingShow; pendingShow = null;
                            if (!disposed && next != null) next();
                        }
                    }
                    if (chosen != null) { lock (sync) { if (disposed) return; } try { post(chosen); } catch (InvalidOperationException) { } }
                });
                thread.IsBackground = true; thread.Name = "AudioSwitch tray popup";
                thread.SetApartmentState(ApartmentState.STA); thread.Start();
            }
        }
        internal void Update(IList<TrayCommand> commands)
        {
            lock (sync) {
                var current = popup;
                if (disposed || current == null || !current.IsHandleCreated || current.IsDisposed) return;
                try { current.BeginInvoke((Action)(() => { if (!current.IsDisposed) current.UpdateSnapshot(commands); })); } catch (InvalidOperationException) { }
            }
        }
        private void Dispatch(TrayCommand command, Action<IList<TrayCommand>, string> complete)
        {
            lock (sync) {
                if (disposed) { complete(null, "声间正在退出。"); return; }
                if (commandPending) { complete(null, "请等待上一次操作完成。"); return; }
                commandPending = true;
            }
            try { post(() => {
                string message = null; IList<TrayCommand> next = null;
                try { command.Execute(); } catch (Exception ex) { message = ex.Message; }
                try { if (snapshot != null) next = snapshot(); } catch (Exception ex) { message = message ?? ex.Message; }
                lock (sync) commandPending = false;
                complete(next, message ?? (next == null ? "状态未能刷新，请重新打开菜单。" : null));
            }); } catch (Exception ex) { lock (sync) commandPending = false; complete(null, ex.Message); }
        }
        private void CloseCurrent()
        {
            var current = popup;
            if (current == null || !current.IsHandleCreated || current.IsDisposed) return;
            try { current.BeginInvoke((Action)(() => current.Close())); } catch (InvalidOperationException) { }
        }
        public void Dispose() { lock (sync) { disposed = true; pendingShow = null; CloseCurrent(); } }
    }

    internal sealed class TrayPopup : Form
    {
        private IList<TrayCommand> home;
        private readonly Action<TrayCommand, Action<IList<TrayCommand>, string>> dispatch;
        private readonly Label status = new Label();
        private readonly Point anchor;
        private readonly PopupViewport list;
        private readonly ToolTip tooltips = new ToolTip { InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 10000 };
        private readonly PopupRow back;
        private readonly List<PopupRow> rows = new List<PopupRow>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Font regular, strong, caption;
        private readonly PopupTheme theme;
        private readonly float scale;
        private readonly bool motion;
        private TrayCommand page;
        private double opened, openingFrom, closingAt, closingOpacity, pageStarted, pageFrom;
        private double presentationOpacity = 1;
        private string pendingKey;
        private bool pageMoving;
        private double pageAmount = 1, pageDuration = 250;
        private int pageDistance;
        private Rectangle pageBoundsFrom, pageBoundsTo;
        private int pageHeader, pageFooter, pageListHeight, pageContentHeight;
        private bool closing, shown;
        private int topColumn;
        private int navigationVersion;
        internal bool KeyboardNavigation { get; private set; }
        internal Action Chosen { get; private set; }
        internal bool Animating { get { return timer.Enabled; } }
        internal bool Busy { get { return pendingKey != null; } }
        internal bool Closing { get { return closing; } }
        internal double PresentationOpacity { get { return presentationOpacity; } }
        internal IList<PopupRow> Rows { get { return rows.AsReadOnly(); } }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        internal TrayPopup(IList<TrayCommand> commands, Point anchor, bool dark, float textScale = 1, bool? animations = null, Action<TrayCommand, Action<IList<TrayCommand>, string>> dispatch = null)
        {
            home = commands; this.anchor = anchor; this.dispatch = dispatch; theme = new PopupTheme(dark);
            // Do not create a Form handle before its styles, controls and lifecycle hooks exist.
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = textScale * g.DpiX / 96F;
            regular = new Font("Microsoft YaHei UI", 10.5F * textScale);
            strong = new Font(regular, FontStyle.Bold); caption = new Font("Microsoft YaHei UI", 9F * textScale);
            Font = regular; AutoScaleMode = AutoScaleMode.None;
            Text = "声间 快捷菜单"; AccessibleName = Text;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = theme.Card; ForeColor = theme.Text; KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            int enabled;
            motion = animations ?? (SystemInformation.IsMenuAnimationEnabled && SystemParametersInfo(0x1042, 0, out enabled, 0) && enabled != 0);
            list = new PopupViewport(theme, scale);
            presentationOpacity = motion ? 0 : 1;
            back = new PopupRow(this, new TrayCommand("返回快捷菜单", TrayGlyph.Back), theme, regular, caption, scale, false, true);
            back.TabIndex = 0; Controls.Add(back); tooltips.SetToolTip(back, "返回");
            Controls.Add(list);
            status.Font = caption; status.ForeColor = ColorTranslator.FromHtml(dark ? "#ff9592" : "#ce2c31"); status.AccessibleRole = AccessibleRole.Alert;
            status.Visible = false; Controls.Add(status);
            timer.Tick += TickAnimation;
            BuildPage();
        }
        private int S(float value) { return (int)Math.Round(value * scale); }
        internal void Navigate(TrayCommand command)
        {
            if (closing || command.Children == null) return;
            navigationVersion++;
            page = command; StartPageMotion(1); BuildPage(); FocusFirst(); pageStarted = Now;
        }
        internal void GoBack()
        {
            if (page == null || closing) return;
            navigationVersion++;
            var previous = page; page = null; StartPageMotion(-1); BuildPage(); pageStarted = Now;
            var row = rows.FirstOrDefault(r => Object.ReferenceEquals(r.Command, previous));
            if (row != null) row.Focus();
        }
        internal void UpdateSnapshot(IList<TrayCommand> commands)
        {
            if (commands == null || IsDisposed) return;
            string pageKey = page == null ? null : page.Key;
            var nextPage = pageKey == null ? null : commands.FirstOrDefault(c => c.Key == pageKey);
            var next = nextPage == null ? commands : nextPage.Children;
            bool sameRows = (page == null) == (nextPage == null) && rows.Count == next.Count && rows.Select(r => r.Command.Key).SequenceEqual(next.Select(c => c.Key));
            var focus = rows.FirstOrDefault(r => r.Focused); string focusKey = focus == null ? null : focus.Command.Key;
            int offset = list.Offset;
            home = commands; page = nextPage;
            if (sameRows) {
                for (int i = 0; i < rows.Count; i++) { rows[i].UpdateCommand(next[i]); tooltips.SetToolTip(rows[i], next[i].Text + (next[i].Detail == null ? "" : "\n" + next[i].Detail)); }
            } else {
                BuildPage();
                var restored = rows.FirstOrDefault(r => r.Command.Key == focusKey && r.Enabled);
                if (restored != null && !closing) restored.Focus();
                list.ScrollTo(offset);
            }
        }
        private void SetStatus(string message)
        {
            message = message ?? "";
            if (status.Text == message) return;
            status.Text = message; status.AccessibleDescription = message; tooltips.SetToolTip(status, message);
            LayoutPage(); RepaintSurface();
        }
        private void BuildPage()
        {
            SuspendLayout(); list.Content.SuspendLayout();
            list.SuspendLayout();
            foreach (var row in rows) { tooltips.SetToolTip(row, null); list.Content.Controls.Remove(row); row.Dispose(); }
            rows.Clear(); list.ScrollTo(0);
            int width = S(288), y = 0;
            var entries = page == null ? home : page.Children;
            for (int i = 0; i < entries.Count; i++) {
                var item = entries[i];
                bool tile = page == null && i < 2;
                if (item.BreakBefore) y += S(6);
                int height = S(String.IsNullOrEmpty(item.Detail) ? 36 : 50);
                var row = new PopupRow(this, item, theme, regular, caption, scale, tile);
                row.Bounds = tile ? new Rectangle(i * (width + S(6)) / 2, 0, (width - S(6)) / 2, height) : new Rectangle(0, y, width, height);
                row.TabIndex = i; rows.Add(row); list.Content.Controls.Add(row);
                if (item.Text.Length > 18 || (item.Detail != null && item.Detail.Length > 18)) tooltips.SetToolTip(row, item.Text + (item.Detail == null ? "" : "\n" + item.Detail));
                if (!tile || i == 1) y += height;
            }
            LayoutPage();
            list.Content.ResumeLayout(false); list.ResumeLayout(false); ResumeLayout(false);
            RepaintSurface();
        }
        private void RepaintSurface()
        {
            // Layered windows retain a composited surface: repaint every child after resizing,
            // including controls that were below the old, shorter viewport's clipping area.
            Invalidate(true);
            if (IsHandleCreated) RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100);
        }
        private void LayoutPage()
        {
            int width = S(288), y = rows.Count == 0 ? 0 : rows.Max(r => r.Bottom);
            var area = Screen.FromPoint(anchor).WorkingArea;
            int statusHeight = String.IsNullOrEmpty(status.Text) ? 0 : Math.Min(S(84), TextRenderer.MeasureText(status.Text, caption, new Size(width, 0), TextFormatFlags.WordBreak).Height) + S(8);
            int header = S(page == null ? 6 : 40), footer = S(6) + statusHeight, available = Math.Max(S(90), area.Height - S(24) - header - footer);
            if (page != null) available = Math.Min(available, S(288));
            pageHeader = header; pageFooter = footer; pageListHeight = Math.Min(y, available); pageContentHeight = y;
            for (int i = 0; i < rows.Count; i++) rows[i].Width = (page == null && i < 2 ? (width - S(6)) / 2 : width) - (y > available ? S(14) : 0);
            var size = new Size(width + S(12), header + pageListHeight + footer);
            var location = new Point(Math.Max(area.Left, Math.Min(anchor.X - size.Width, area.Right - size.Width)), Math.Max(area.Top, Math.Min(anchor.Y - size.Height, area.Bottom - size.Height)));
            pageBoundsFrom = Bounds; pageBoundsTo = new Rectangle(location, size);
            if (!pageMoving) Bounds = pageBoundsTo;
            else pageStarted = Now;
            LayoutViewport();
            back.Bounds = new Rectangle(S(10), S(6), S(28), S(28)); back.Visible = page != null;
            status.Visible = statusHeight != 0;
            Invalidate();
            if (IsHandleCreated) PopupChrome.Round(Handle);
        }
        private void LayoutViewport()
        {
            // Keep the content clipped inside the currently visible border throughout a resize.
            // Use the destination's scroll state so a short page does not flash a temporary rail.
            list.Bounds = new Rectangle(S(6), pageHeader, S(288), Math.Max(0, Math.Min(pageListHeight, Height - pageHeader - pageFooter)));
            list.Configure(pageContentHeight, pageContentHeight > pageListHeight);
            status.SetBounds(S(6), Height - pageFooter + S(4), S(288), Math.Max(0, pageFooter - S(10)));
        }
        private void AdvancePageBounds(double t)
        {
            // Smooth size changes without bounce, retaining the tray-side anchor. Retargets
            // start from Bounds (the visible frame), not the previous destination rectangle.
            double progress = t * t * (3 - 2 * t);
            var next = new Rectangle(
                pageBoundsFrom.X + (int)Math.Round((pageBoundsTo.X - pageBoundsFrom.X) * progress),
                pageBoundsFrom.Y + (int)Math.Round((pageBoundsTo.Y - pageBoundsFrom.Y) * progress),
                pageBoundsFrom.Width + (int)Math.Round((pageBoundsTo.Width - pageBoundsFrom.Width) * progress),
                pageBoundsFrom.Height + (int)Math.Round((pageBoundsTo.Height - pageBoundsFrom.Height) * progress));
            if (Bounds == next) return;
            SuspendLayout(); list.SuspendLayout();
            Bounds = next; LayoutViewport();
            list.ResumeLayout(false); ResumeLayout(false);
            RepaintSurface();
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PopupChrome.Round(Handle); SetOpacity(presentationOpacity);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // SetWindowRgn stores fixed pixel bounds; resizing the HWND does not grow that mask.
            // Refresh it on every size change, including intermediate animation frames.
            if (IsHandleCreated) PopupChrome.Round(Handle);
        }
        private void SetOpacity(double value)
        {
            presentationOpacity = Math.Max(0, Math.Min(1, value));
            if (IsHandleCreated && !SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(presentationOpacity * 255), 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override void SetVisibleCore(bool value)
        {
            if (value && !IsDisposed) {
                // Match native notification menus: acquire foreground before showing, so
                // Explorer's overflow flyout can yield instead of covering our first frame.
                SetForegroundWindow(Handle);
            }
            base.SetVisibleCore(value);
        }
        protected override void OnShown(EventArgs e)
        {
            PopupChrome.Round(Handle);
            // TopMost alone does not reorder two existing topmost windows.
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002);
            SetForegroundWindow(Handle); Activate();
            // Paint a complete frame while alpha is zero; never switch layered styles at alpha 255.
            Refresh(); shown = true; opened = clock.Elapsed.TotalMilliseconds; openingFrom = presentationOpacity;
            if (motion) timer.Start();
            base.OnShown(e);
        }
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x80 | 0x80000; return value; }
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (shown) Dismiss(); }
        internal void Dismiss()
        {
            if (closing || IsDisposed) return;
            if (!motion) { Close(); return; }
            closing = true; closingAt = clock.Elapsed.TotalMilliseconds; closingOpacity = presentationOpacity;
            timer.Start();
        }
        internal void Reopen()
        {
            if (IsDisposed || Chosen != null) return;
            if (closing) { closing = false; opened = Now; openingFrom = presentationOpacity; if (motion) timer.Start(); else SetOpacity(1); }
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002); SetForegroundWindow(Handle); Activate();
        }
        internal void ActivateCommand(TrayCommand command)
        {
            if (closing || !command.Enabled || Chosen != null) return;
            if (command.Children != null) { Navigate(command); return; }
            if (command.Execute == null) return;
            if (command.KeepOpen) {
                if (Busy) return;
                if (dispatch == null) { SetStatus("当前菜单未连接后台，请重新打开。"); return; }
                pendingKey = command.Key; SetStatus(null);
                int version = navigationVersion;
                foreach (var row in rows) row.UpdateCommand(row.Command);
                try { dispatch(command, (next, message) => {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { BeginInvoke((Action)(() => {
                        if (IsDisposed) return;
                        string completedKey = pendingKey; pendingKey = null;
                        if (next != null) UpdateSnapshot(next);
                        else foreach (var row in rows) row.UpdateCommand(row.Command);
                        SetStatus(message);
                        var focus = rows.FirstOrDefault(r => r.Command.Key == completedKey && r.Enabled);
                        if (!closing && version == navigationVersion && focus != null) focus.Focus();
                    })); } catch (InvalidOperationException) { }
                }); } catch (Exception ex) { pendingKey = null; foreach (var row in rows) row.UpdateCommand(row.Command); SetStatus(ex.Message); }
                return;
            }
            // Opening another surface or exiting still releases this transient window.
            Chosen = command.Execute;
            if (command.AnimateClose) Dismiss(); else Close();
        }
        private void FocusFirst() { var row = rows.FirstOrDefault(r => r.Enabled); if (row != null) row.Focus(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { return HandleNavigation(keyData) || base.ProcessCmdKey(ref msg, keyData); }
        internal bool HandleNavigation(Keys keyData)
        {
            navigationVersion++;
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Tab || key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right || key == Keys.Back || key == Keys.Home || key == Keys.End || key == Keys.Enter || key == Keys.Space)
                SetKeyboardNavigation(true);
            if (keyData == Keys.Escape) { Dismiss(); return true; }
            var active = rows.FirstOrDefault(r => r.Focused);
            int activeIndex = rows.IndexOf(active);
            bool onTopRow = page == null && activeIndex >= 0 && activeIndex < 2;
            if (onTopRow) topColumn = activeIndex;
            if (onTopRow && (keyData == Keys.Left || keyData == Keys.Right)) {
                int column = keyData == Keys.Left ? 0 : 1;
                if (column < rows.Count && rows[column].Enabled) { topColumn = column; rows[column].Focus(); }
                return true;
            }
            if (keyData == Keys.Left || keyData == Keys.Back) { GoBack(); return true; }
            if (keyData == Keys.Right && active != null && active.Command.Children != null) { Navigate(active.Command); return true; }
            if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Home || keyData == Keys.End) {
                // The first two actions form one visual row, not two vertical menu entries.
                var enabled = rows.Where(r => r.Enabled && (page != null || rows.IndexOf(r) >= 2)).ToList();
                if (page == null) {
                    var top = rows.Take(2).Where(r => r.Enabled).ToList();
                    var preferred = keyData == Keys.Home ? top.FirstOrDefault() : top.FirstOrDefault(r => rows.IndexOf(r) == topColumn) ?? top.FirstOrDefault();
                    if (preferred != null) enabled.Insert(0, preferred);
                }
                if (enabled.Count == 0) return true;
                int index = onTopRow ? 0 : enabled.IndexOf(active);
                if (index < 0 && keyData == Keys.Up) index = 0;
                index = keyData == Keys.Home ? 0 : keyData == Keys.End ? enabled.Count - 1 : (index + (keyData == Keys.Up ? -1 : 1) + enabled.Count) % enabled.Count;
                enabled[index].Focus(); list.Reveal(enabled[index]); return true;
            }
            if (keyData == Keys.Enter) { if (back.Visible && back.Focused) back.PerformClick(); else if (active == null) FocusFirst(); else active.PerformClick(); return true; }
            return false;
        }
        internal void SetKeyboardNavigation(bool value)
        {
            if (KeyboardNavigation == value) return;
            KeyboardNavigation = value;
            foreach (var row in rows) { if (value) row.ClearHover(); row.Invalidate(); }
            if (value) back.ClearHover(); back.Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Palette.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), S(12)))
            using (var pen = new Pen(theme.Border)) e.Graphics.DrawPath(pen, path);
            if (page != null) TextRenderer.DrawText(e.Graphics, page.Text, strong, new Rectangle(S(44), S(4), Width - S(60), S(32)), theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        private void StartPageMotion(int direction)
        {
            if (!motion || !shown) return;
            // Move the whole content layer inside a fixed viewport, never individual labels.
            // The new page enters from its own direction even if the preceding one was interrupted.
            pageDistance = direction * S(24);
            list.SetTransitionOffset(pageDistance);
            pageFrom = pageMoving ? pageAmount : .8;
            pageDuration = direction > 0 ? 250 : 200;
            pageStarted = Now; pageMoving = true; pageAmount = pageFrom; timer.Start();
        }
        internal double Now { get { return clock.Elapsed.TotalMilliseconds; } }
        internal bool Motion { get { return motion; } }
        internal void Animate() { if (!IsDisposed && !closing && shown && motion) timer.Start(); }
        internal void Reveal(PopupRow row) { list.Reveal(row); }
        internal void ScrollRows(int delta) { list.ScrollTo(list.Offset - delta / 120 * S(48)); }
        private void TickAnimation(object sender, EventArgs e)
        {
            double now = Now;
            if (!shown) { timer.Stop(); return; }
            if (closing) {
                foreach (var row in rows) row.Advance(now);
                back.Advance(now);
                double t = Math.Min(1, (now - closingAt) / 200);
                SetOpacity(closingOpacity * (1 - t * t));
                if (t >= 1) Close();
                return;
            }
            bool running = false;
            if (motion && now - opened < 250) { double t = Math.Min(1, (now - opened) / 250); SetOpacity(1 - (1 - openingFrom) * Math.Pow(1 - t, 3)); running = true; }
            else if (presentationOpacity != 1) SetOpacity(1);
            if (pageMoving) {
                double t = Math.Min(1, (now - pageStarted) / pageDuration);
                pageAmount = 1 - (1 - pageFrom) * Math.Pow(1 - t, 3);
                list.SetTransitionOffset((int)Math.Round(pageDistance * Math.Pow(1 - t, 3)));
                AdvancePageBounds(t);
                Invalidate(true);
                pageMoving = t < 1; running |= pageMoving;
                if (!pageMoving) RepaintSurface();
            }
            foreach (var row in rows) running |= row.Advance(now);
            running |= back.Advance(now);
            if (!running) timer.Stop();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Stop();
            base.Dispose(disposing);
            if (disposing) { timer.Dispose(); tooltips.Dispose(); regular.Dispose(); strong.Dispose(); caption.Dispose(); }
        }
        private sealed class BufferedPanel : Panel { internal BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }
        private sealed class PopupViewport : Panel
        {
            internal readonly Panel Content = new BufferedPanel();
            private readonly ScrollThumb rail;
            private readonly int railWidth;
            private int total, transitionOffset;
            internal int Offset { get; private set; }
            internal PopupViewport(PopupTheme theme, float scale)
            {
                DoubleBuffered = true; ResizeRedraw = true; BackColor = Content.BackColor = theme.Card; railWidth = (int)Math.Round(14 * scale);
                rail = new ScrollThumb(this, theme, scale); Controls.Add(Content); Controls.Add(rail);
            }
            internal void Configure(int height, bool scrollable)
            {
                total = height; rail.Visible = scrollable;
                Content.SetBounds(transitionOffset, -Offset, Width - (rail.Visible ? railWidth : 0), Math.Max(Height, total));
                rail.SetBounds(Width - railWidth, 0, railWidth, Height); rail.BringToFront(); ScrollTo(Offset);
            }
            internal void ScrollTo(int value)
            {
                Offset = Math.Max(0, Math.Min(Math.Max(0, total - Height), value));
                Content.Top = -Offset; rail.Invalidate(); Content.Invalidate(true);
            }
            internal void SetTransitionOffset(int value)
            {
                if (transitionOffset == value) return;
                transitionOffset = value; Content.Left = value;
                Invalidate(true);
            }
            internal void Reveal(Control row)
            {
                if (row.Top < Offset) ScrollTo(row.Top);
                else if (row.Bottom > Offset + Height) ScrollTo(row.Bottom - Height);
            }
            protected override void OnMouseWheel(MouseEventArgs e) { ScrollTo(Offset - e.Delta / 120 * 48); var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; }
            private sealed class ScrollThumb : Control
            {
                private readonly PopupViewport viewport;
                private readonly PopupTheme theme;
                private readonly float scale;
                private bool dragging, hover;
                private float grab;
                internal ScrollThumb(PopupViewport viewport, PopupTheme theme, float scale)
                {
                    this.viewport = viewport; this.theme = theme; this.scale = scale; BackColor = theme.Card;
                    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                    AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "设备列表滚动条"; TabStop = false;
                }
                private RectangleF Thumb()
                {
                    float height = Math.Min(Height, Math.Max(24 * scale, Height * (float)Height / Math.Max(1, viewport.total)));
                    float y = (Height - height) * viewport.Offset / Math.Max(1, viewport.total - viewport.Height);
                    return new RectangleF((Width - 4 * scale) / 2, y, 4 * scale, height);
                }
                protected override void OnPaint(PaintEventArgs e)
                {
                    e.Graphics.Clear(theme.Card); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = Palette.Round(Thumb(), 2 * scale)) using (var brush = new SolidBrush(hover || dragging ? theme.Muted : theme.Border)) e.Graphics.FillPath(brush, path);
                }
                protected override void OnMouseDown(MouseEventArgs e)
                {
                    base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
                    var thumb = Thumb();
                    if (e.Y >= thumb.Top && e.Y <= thumb.Bottom) { dragging = true; grab = e.Y - thumb.Top; Capture = true; Invalidate(); }
                    else viewport.ScrollTo(viewport.Offset + (e.Y < thumb.Top ? -viewport.Height : viewport.Height));
                }
                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e); if (!dragging) return;
                    viewport.ScrollTo((int)((e.Y - grab) / Math.Max(1, Height - Thumb().Height) * (viewport.total - viewport.Height)));
                }
                protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }
                protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; Invalidate(); } }
                protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
                protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            }
        }
        internal sealed class PopupTheme
        {
            internal readonly Color Card, Text, Muted, Border, Hover, Pressed, Accent, Selected, AccentText;
            internal PopupTheme(bool dark) {
                Card = Hex(dark ? "#18191b" : "#f9f9fb"); Text = Hex(dark ? "#edeef0" : "#1c2024"); Muted = Hex(dark ? "#b0b4ba" : "#60646c");
                Border = Hex(dark ? "#363a3f" : "#d9d9e0"); Hover = Hex(dark ? "#272a2d" : "#e8e8ec"); Pressed = Hex(dark ? "#2e3135" : "#e0e1e6");
                Accent = Hex("#3e63dd"); Selected = Hex(dark ? "#182449" : "#edf2fe"); AccentText = Hex(dark ? "#9eb1ff" : "#3a5bc7");
            }
            private static Color Hex(string text) { return ColorTranslator.FromHtml(text); }
            internal static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        }
        internal sealed class PopupRow : Button
        {
            internal TrayCommand Command { get; private set; }
            private readonly TrayPopup owner;
            private readonly PopupTheme theme;
            private readonly Font caption;
            private readonly float scale;
            private readonly bool tile, iconOnly;
            private Bitmap icon;
            private bool hover, pressed;
            private double from, target, started, amount;
            private double pressFrom, pressStarted, pressAmount;
            private double selectionFrom, selectionStarted, selectionAmount, selectionTarget;
            internal PopupRow(TrayPopup owner, TrayCommand command, PopupTheme theme, Font font, Font caption, float scale, bool tile, bool iconOnly = false)
            {
                this.owner = owner; Command = command; this.theme = theme; this.caption = caption; this.scale = scale; this.tile = tile; this.iconOnly = iconOnly;
                Font = font; Text = command.Text; AccessibleName = command.Text; AccessibleDescription = command.Detail;
                Enabled = command.Enabled; BackColor = theme.Card; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                selectionFrom = selectionAmount = selectionTarget = command.Checked ? 1 : 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                icon = new Bitmap(S(42), S(42)); using (var g = Graphics.FromImage(icon)) TrayArt.DrawGlyph(g, command.Glyph, scale, icon.Height, command.Checked ? theme.AccentText : theme.Muted);
                Click += delegate { if (iconOnly) owner.GoBack(); else owner.ActivateCommand(Command); };
            }
            internal void UpdateCommand(TrayCommand command)
            {
                bool changed = Command.Checked != command.Checked || Command.Glyph != command.Glyph;
                if (Command.Checked != command.Checked) {
                    selectionFrom = selectionAmount; selectionTarget = command.Checked ? 1 : 0; selectionStarted = owner.Now;
                    if (!owner.Motion) selectionAmount = selectionTarget;
                    owner.Animate();
                }
                Command = command; Text = command.Text; AccessibleName = command.Text; AccessibleDescription = command.Detail;
                Enabled = command.Enabled;
                if (changed) {
                    icon.Dispose(); icon = new Bitmap(S(42), S(42));
                    using (var g = Graphics.FromImage(icon)) TrayArt.DrawGlyph(g, command.Glyph, scale, icon.Height, command.Checked ? theme.AccentText : theme.Muted);
                }
                Invalidate();
            }
            private int S(float value) { return (int)Math.Round(value * scale); }
            private double At(double now) { double t = Math.Max(0, Math.Min(1, (now - started) / 150)); return from + (target - from) * (1 - Math.Pow(1 - t, 3)); }
            private void Retarget() { double now = owner.Now; from = amount; target = hover ? 1 : 0; started = now; if (!owner.Motion) amount = target; owner.Animate(); Invalidate(); }
            internal void ClearHover() { if (hover) { hover = false; Retarget(); } }
            private void SetPressed(bool value)
            {
                if (pressed == value) return;
                pressed = value; pressFrom = pressAmount; pressStarted = owner.Now;
                // Respond on mouse-down immediately, then release smoothly without delaying Click.
                if (value) pressAmount = 1; else if (!owner.Motion) pressAmount = 0;
                owner.Animate(); Invalidate();
            }
            internal bool Advance(double now)
            {
                double next = owner.Motion ? At(now) : target;
                double nextPress = pressed ? 1 : owner.Motion ? pressFrom * Math.Pow(1 - Math.Min(1, (now - pressStarted) / 120), 3) : 0;
                double t = Math.Max(0, Math.Min(1, (now - selectionStarted) / 160));
                double nextSelection = owner.Motion ? selectionFrom + (selectionTarget - selectionFrom) * (1 - Math.Pow(1 - t, 3)) : selectionTarget;
                if (next != amount || nextPress != pressAmount || nextSelection != selectionAmount) { amount = next; pressAmount = nextPress; selectionAmount = nextSelection; Invalidate(); }
                return owner.Motion && ((now - started < 150 && from != target) || (!pressed && now - pressStarted < 120 && pressFrom != 0) || (t < 1 && selectionFrom != selectionTarget));
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); owner.SetKeyboardNavigation(false); hover = true; Retarget(); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); owner.SetKeyboardNavigation(false); if (!hover) { hover = true; Retarget(); } }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); ClearHover(); SetPressed(false); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (!iconOnly) owner.Reveal(this); Invalidate(); }
            protected override void OnMouseWheel(MouseEventArgs e) { owner.ScrollRows(e.Delta); var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); SetPressed(false); Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { if (owner.Closing) return; owner.SetKeyboardNavigation(false); base.OnMouseDown(e); SetPressed(e.Button == MouseButtons.Left); }
            protected override void OnMouseUp(MouseEventArgs e) { SetPressed(false); base.OnMouseUp(e); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) SetPressed(false); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) SetPressed(true); base.OnKeyDown(e); }
            protected override void OnKeyUp(KeyEventArgs e) { SetPressed(false); base.OnKeyUp(e); }
            protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { hover = pressed = false; amount = target = from = pressAmount = pressFrom = 0; } }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(theme.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
                const int offset = 0;
                Color resting = Command.Toggle ? theme.Card : PopupTheme.Mix(theme.Card, theme.Selected, selectionAmount);
                Color hovered = Command.Toggle ? theme.Hover : PopupTheme.Mix(theme.Hover, theme.Selected, selectionAmount);
                bool focusRing = Focused && owner.KeyboardNavigation;
                using (var path = Palette.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), S(6))) {
                    using (var brush = new SolidBrush(PopupTheme.Mix(PopupTheme.Mix(resting, hovered, amount), theme.Pressed, pressAmount))) g.FillPath(brush, path);
                    if (tile || focusRing) using (var pen = new Pen(focusRing ? theme.Accent : theme.Border)) g.DrawPath(pen, path);
                }
                g.DrawImageUnscaled(icon, iconOnly ? (Width - icon.Width) / 2 : -S(3) + offset, (Height - icon.Height) / 2);
                if (iconOnly) return;
                int right = Command.Toggle ? S(54) : Command.Children != null || Command.KeepOpen || Command.Checked ? S(28) : S(10);
                var bounds = new Rectangle(S(36) + offset, 0, Math.Max(1, Width - S(36) - right), Height);
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                Color text = Enabled ? theme.Text : theme.Muted;
                if (String.IsNullOrEmpty(Command.Detail)) TextRenderer.DrawText(g, Text, Font, bounds, text, flags);
                else {
                    TextRenderer.DrawText(g, Text, Font, new Rectangle(bounds.X, S(4), bounds.Width, S(22)), text, flags);
                    TextRenderer.DrawText(g, Command.Detail, caption, new Rectangle(bounds.X, S(25), bounds.Width, S(18)), theme.Muted, flags);
                }
                if (!Command.Toggle && owner.pendingKey != null && owner.pendingKey == Command.Key) {
                    TextRenderer.DrawText(g, "…", Font, new Rectangle(Width - S(30) + offset, 0, S(22), Height), theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                } else if (Command.Toggle) {
                    var track = new RectangleF(Width - S(44) + offset, Height / 2F - S(9), S(32), S(18));
                    using (var path = Palette.Round(track, S(9))) using (var brush = new SolidBrush(PopupTheme.Mix(theme.Border, theme.Accent, selectionAmount))) g.FillPath(brush, path);
                    using (var brush = new SolidBrush(PopupTheme.Mix(theme.Muted, Color.White, selectionAmount))) g.FillEllipse(brush, track.X + S(2) + (float)(S(14) * selectionAmount), track.Y + S(2), S(14), S(14));
                } else if (Command.Children != null || selectionAmount > 0) using (var pen = new Pen(Command.Children != null ? theme.Muted : PopupTheme.Mix(resting, theme.AccentText, selectionAmount), 1.5F * scale)) {
                    pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                    float x = Width - S(18) + offset, y = Height / 2F;
                    g.DrawLines(pen, Command.Children == null ? new[] { new PointF(x - S(4), y), new PointF(x - S(1), y + S(3)), new PointF(x + S(5), y - S(4)) }
                        : new[] { new PointF(x - S(2), y - S(3)), new PointF(x + S(1), y), new PointF(x - S(2), y + S(3)) });
                }
                PaintPageFade(g);
            }
            private void PaintPageFade(Graphics graphics)
            {
                if (owner.pageAmount >= 1) return;
                using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(255 * (1 - owner.pageAmount)), theme.Card))) graphics.FillRectangle(brush, ClientRectangle);
            }
            protected override AccessibleObject CreateAccessibilityInstance() { return new RowAccessibility(this); }
            private sealed class RowAccessibility : ControlAccessibleObject
            {
                private readonly PopupRow row;
                internal RowAccessibility(PopupRow row) : base(row) { this.row = row; }
                public override AccessibleRole Role { get { return row.Command.Toggle ? AccessibleRole.CheckButton : AccessibleRole.PushButton; } }
                public override AccessibleStates State { get { return base.State | (row.Command.Checked ? AccessibleStates.Checked : AccessibleStates.None) | (row.owner.Closing || (row.owner.Busy && row.Command.KeepOpen) ? AccessibleStates.Unavailable : AccessibleStates.None); } }
                public override string DefaultAction { get { return row.Command.Children != null ? "打开" : "选择"; } }
                public override void DoDefaultAction() { if (row.Enabled) row.PerformClick(); }
            }
            protected override void Dispose(bool disposing) { if (disposing) icon.Dispose(); base.Dispose(disposing); }
        }
    }
}
