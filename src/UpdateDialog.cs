// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class UpdateDialog : Form
    {
        private readonly Label status;
        private readonly TextBox notes;
        private readonly FlatAction action;
        private readonly FlatAction close;
        private readonly ProgressBar progress;
        private readonly CheckBox automaticUpdates;
        private readonly Label automaticHint;
        private readonly Func<Request, Task<Reply>> send;
        private bool applyingPreference;
        private bool savingPreference;
        private bool preferenceLoaded;
        private bool confirmedAutomaticUpdates = true;
        private CancellationTokenSource cancellation;
        private UpdateRelease release;
        private bool handingOff;
        internal bool Restarting { get; private set; }
        internal UpdateDialog(bool preview = false, Func<Request, Task<Reply>> send = null)
        {
            this.send = send ?? (request => Task.Run(() => Wire.Send(request)));
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "软件更新 · 声间"; Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Palette.Background; ForeColor = Palette.Text;
            ClientSize = new Size(580, 484); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var title = Palette.Label("软件更新", 18, Palette.Text, true); title.SetBounds(24, 22, 510, 38); Controls.Add(title);
            var current = Palette.Label("当前版本  v" + AppVersion.Number + "  ·  更新来源：GitHub", 9, Palette.Muted, false); current.SetBounds(26, 66, 528, 28); Controls.Add(current);
            automaticUpdates = new SwitchOption { Name = "automaticUpdates", Text = "自动更新", AccessibleName = "自动更新", Checked = true, Enabled = preview && send == null };
            automaticUpdates.SetBounds(26, 102, 184, 32); Controls.Add(automaticUpdates);
            automaticHint = Palette.Label("默认开启；空闲时下载，下次启动应用。关闭后仍可手动更新。", 8, Palette.Muted, false);
            automaticHint.Name = "automaticUpdateHint"; automaticHint.SetBounds(26, 136, 528, 32); Controls.Add(automaticHint);
            automaticUpdates.CheckedChanged += async delegate { if (!applyingPreference && preferenceLoaded && !savingPreference) await SaveAutomaticUpdates(); };
            status = Palette.Label("点击检查更新，获取最新正式版本。", 9, Palette.Text, false); status.SetBounds(26, 176, 528, 55); Controls.Add(status);
            var notesCard = new Surface { Name = "updateNotesCard", Padding = new Padding(14, 12, 10, 12) };
            notesCard.SetBounds(26, 240, 528, 130); Controls.Add(notesCard);
            notes = new ReleaseNotesBox { Name = "updateNotes", AccessibleName = "更新内容", Dock = DockStyle.Fill,
                BackColor = Palette.Card, ForeColor = Palette.Text, Text = "更新时会短暂退出并重新打开声间。设备设置和预设会保留，旧程序会备份。", TabStop = true };
            notesCard.Controls.Add(notes);
            progress = new ProgressBar { Minimum = 0, Maximum = 100, Visible = false }; progress.SetBounds(26, 385, 528, 8); Controls.Add(progress);
            var website = new FlatAction("打开发布页", false); website.SetBounds(26, 416, 112, 38); Controls.Add(website);
            website.Click += delegate { try { Process.Start(new ProcessStartInfo(AppUpdate.ReleasesUrl) { UseShellExecute = true }); } catch (Exception ex) { status.Text = "无法打开浏览器：" + ex.Message; } };
            close = new FlatAction("关闭", false); close.SetBounds(298, 416, 104, 38); Controls.Add(close); CancelButton = close;
            close.Click += delegate { Close(); };
            action = new FlatAction("检查更新", true) { Name = "checkUpdates" }; action.SetBounds(414, 416, 140, 38); Controls.Add(action);
            action.Click += async delegate { if (!preview) await Execute(); };
            ActiveControl = close;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (handingOff && !Restarting) { e.Cancel = true; return; } if (cancellation != null) cancellation.Cancel(); };
            Shown += async delegate {
                Palette.ChromeTree(this);
                if (!preview || send != null) await LoadAutomaticUpdates();
                if (!preview && !IsDisposed) await Execute();
            };
            ResumeLayout(true);
        }
        private void ApplyAutomaticUpdates(Reply reply)
        {
            if (reply == null || reply.State == null || reply.Preferences == null) throw new InvalidOperationException("未能读取设置，请关闭后重新打开此窗口。");
            confirmedAutomaticUpdates = reply.Preferences.AutoUpdateEnabled;
            applyingPreference = true; automaticUpdates.Checked = confirmedAutomaticUpdates; applyingPreference = false;
            automaticHint.Text = !confirmedAutomaticUpdates ? "自动更新已关闭；仍可点击“检查更新”手动更新。" :
                reply.Preferences.GameMode ? "自动更新已开启；游戏模式期间暂时暂停。" : "空闲时下载，下次启动应用；不会弹窗打断操作。";
            preferenceLoaded = true;
        }
        private async Task LoadAutomaticUpdates()
        {
            try { var reply = await send(new Request { Action = "snapshot" }); if (!IsDisposed) ApplyAutomaticUpdates(reply); }
            catch (Exception) { if (!IsDisposed) automaticHint.Text = "无法读取自动更新设置，请关闭后重新打开此窗口。"; }
            finally { if (!IsDisposed) automaticUpdates.Enabled = preferenceLoaded; }
        }
        private async Task SaveAutomaticUpdates()
        {
            savingPreference = true; automaticUpdates.Enabled = false; action.Enabled = false;
            try
            {
                var reply = await send(new Request { Action = "automaticUpdates", Value = automaticUpdates.Checked });
                if (IsDisposed) return;
                ApplyAutomaticUpdates(reply);
                if (!String.IsNullOrEmpty(reply.Error)) automaticHint.Text = reply.Error;
            }
            catch (Exception)
            {
                if (!IsDisposed)
                {
                    applyingPreference = true; automaticUpdates.Checked = confirmedAutomaticUpdates; applyingPreference = false;
                    automaticHint.Text = "未能确认保存结果，请关闭后重新打开此窗口核对设置。";
                }
            }
            finally
            {
                savingPreference = false;
                if (!IsDisposed) { automaticUpdates.Enabled = preferenceLoaded; action.Enabled = cancellation == null; }
            }
        }
        private sealed class ReleaseNotesBox : TextBox
        {
            internal ReleaseNotesBox()
            {
                Multiline = true; ReadOnly = true; WordWrap = true;
                ScrollBars = ScrollBars.Vertical; BorderStyle = BorderStyle.None;
            }
            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Palette.NativeChrome(this); }
            protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); Palette.NativeChrome(this); }
        }
        private async Task Execute()
        {
            if (cancellation != null || savingPreference || IsDisposed) return;
            var cancel = new CancellationTokenSource(); cancellation = cancel;
            cancel.CancelAfter(release == null ? 30000 : 180000);
            action.Enabled = false; close.Text = "取消";
            // A manual installation must not race with a preference save before restart.
            if (release != null) automaticUpdates.Enabled = false;
            try
            {
                if (release == null)
                {
                    status.Text = "正在检查 GitHub 正式版本…";
                    var found = await Task.Run(() => AppUpdate.Check(cancel.Token));
                    if (IsDisposed || cancel.IsCancellationRequested) return;
                    if (found == null) { status.Text = "暂未找到可用的正式版本，请打开发布页查看。"; return; }
                    if (found.Version <= AppUpdate.ParseVersion(AppVersion.Number))
                    {
                        status.Text = found.Version == AppUpdate.ParseVersion(AppVersion.Number) ? "当前已是最新正式版本（" + found.Tag + "）。" : "本地版本 v" + AppVersion.Number + " 高于最新正式版 " + found.Tag + "，无需更新。";
                        notes.Text = found.Notes ?? "暂无更新说明。";
                        return;
                    }
                    release = found; status.Text = "发现新版本 " + release.Tag + "。下载完成后将退出并重新打开声间。";
                    notes.Text = String.IsNullOrWhiteSpace(release.Notes) ? "发布者没有填写更新说明。" : release.Notes;
                    action.Text = "下载并更新";
                }
                else
                {
                    progress.Visible = true; progress.Value = 0;
                    progress.Style = release.Size > 0 ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
                    status.Text = "正在下载更新包，关闭此窗口可以取消。";
                    int lastPercent = -1;
                    var report = new Progress<long>(bytes => {
                        if (IsDisposed || cancel.IsCancellationRequested) return;
                        if (release.Size <= 0) { status.Text = "正在下载更新包（已接收 " + (bytes / 1024) + " KB），关闭此窗口可以取消。"; return; }
                        int percent = (int)Math.Min(100, bytes * 100 / release.Size);
                        if (percent != lastPercent) { progress.Value = percent; lastPercent = percent; }
                    });
                    string payload = await Task.Run(() => AppUpdate.Prepare(release, cancel.Token, bytes => ((IProgress<long>)report).Report(bytes)));
                    if (IsDisposed || cancel.IsCancellationRequested) return;
                    status.Text = "已通过校验，正在准备重启…"; handingOff = true; close.Enabled = false;
                    int parent = Process.GetCurrentProcess().Id;
                    await Task.Run(() => UpdateInstaller.Launch(payload, release, parent));
                    Restarting = true; DialogResult = DialogResult.OK; Close();
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed) { status.Text = "更新未完成，可重试或打开发布页。"; notes.Text = cancel.IsCancellationRequested ? "连接超时或操作已取消，请检查网络后重试。" : ex.Message; }
            }
            finally
            {
                handingOff = false; cancellation = null; cancel.Dispose();
                if (!IsDisposed) { action.Enabled = !savingPreference; automaticUpdates.Enabled = preferenceLoaded && !savingPreference; close.Enabled = true; close.Text = "关闭"; progress.Visible = false; }
            }
        }
    }
}
