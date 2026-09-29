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
    internal sealed class IntegrityDialog : Form
    {
        private readonly Label status;
        private readonly TextBox details;
        private readonly FlatAction retry, close;
        private readonly bool startup;
        private CancellationTokenSource cancellation;
        private bool handingOff, closeRequested;
        internal bool Restarting { get; private set; }
        internal bool Passed { get; private set; }
        internal IntegrityDialog(bool startup = false, bool preview = false)
        {
            this.startup = startup;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "文件检查与修复 · 声间"; Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Palette.Background; ForeColor = Palette.Text;
            ClientSize = new Size(600, 470); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = startup ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            var title = Palette.Label("文件检查与修复", 18, Palette.Text, true); title.SetBounds(24, 22, 550, 40); Controls.Add(title);
            status = Palette.Label("正在检查运行必需文件…", 9, Palette.Text, false); status.SetBounds(26, 76, 548, 48); Controls.Add(status);
            var card = new Surface { Padding = new Padding(14, 12, 10, 12) }; card.SetBounds(26, 130, 548, 245); Controls.Add(card);
            details = new DetailsBox { Name = "integrityDetails", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Palette.Card, ForeColor = Palette.Text, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                Text = "缺失或损坏的必要文件会自动尝试恢复。说明和许可文件不要求补齐。用户配置和开机自启设置会保留。" };
            card.Controls.Add(details);
            var website = new FlatAction("打开发布页", false); website.SetBounds(26, 405, 112, 38); Controls.Add(website);
            website.Click += delegate { try { Process.Start(new ProcessStartInfo(AppUpdate.ReleasesUrl) { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; } };
            close = new FlatAction(preview ? "关闭" : "取消", false); close.SetBounds(334, 405, 104, 38); Controls.Add(close); CancelButton = close;
            close.Click += delegate { Close(); };
            retry = new FlatAction("重新检查", true) { Name = "retryIntegrity" }; retry.SetBounds(450, 405, 124, 38); Controls.Add(retry);
            retry.Click += async delegate { if (!preview) await Execute(); };
            ActiveControl = close;
            Shown += async delegate { Palette.ChromeTree(this); if (!preview) await Execute(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (Restarting) return;
                if (handingOff) { e.Cancel = true; return; }
                if (cancellation != null) { closeRequested = true; cancellation.Cancel(); status.Text = "正在取消，请稍候…"; e.Cancel = true; }
            };
        }
        private sealed class DetailsBox : TextBox
        {
            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Palette.NativeChrome(this); }
            protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); Palette.NativeChrome(this); }
        }
        internal void RenderResult(RepairResult result)
        {
            Passed = result.Check.Passed;
            status.Text = Passed ? "运行必需文件已齐全，可以正常使用。" : "修复尚未完成，请查看原因后重试。";
            details.Text = result.Message;
            details.Select(0, 0);
        }
        private async Task Execute()
        {
            if (cancellation != null) return;
            var cancel = new CancellationTokenSource(); cancellation = cancel; cancel.CancelAfter(90000);
            retry.Enabled = false; close.Text = "取消";
            bool finish = false;
            try
            {
                var report = new Progress<string>(text => { if (!IsDisposed && !closeRequested) status.Text = text; });
                var result = await Task.Run(() => FileRepair.Run(AppDomain.CurrentDomain.BaseDirectory, cancel.Token, text => ((IProgress<string>)report).Report(text)));
                RenderResult(result);
                if (result.Payload != null && !cancel.IsCancellationRequested)
                {
                    handingOff = true; close.Enabled = false; status.Text = "修复文件已校验，正在准备重启…";
                    int parent = Process.GetCurrentProcess().Id;
                    await Task.Run(() => UpdateInstaller.LaunchRepair(result.Payload, parent));
                    Restarting = true; finish = true;
                }
                else if (startup && Passed && !closeRequested) finish = true;
            }
            catch (Exception ex) { status.Text = "修复未完成，可重试或打开发布页。"; details.Text = ex.Message; }
            finally
            {
                handingOff = false; cancellation = null; cancel.Dispose();
                retry.Enabled = true; close.Enabled = true; close.Text = "关闭";
            }
            if (finish || closeRequested) Close();
        }
    }
}
