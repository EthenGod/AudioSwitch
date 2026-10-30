// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AudioSwitch
{
    public sealed class UpgradeResult
    {
        public string Kind { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string Version { get; set; }
        public string Notes { get; set; }
        public string Directory { get; set; }
        public string[] Entries { get; set; }
        public long Bytes { get; set; }
        public long Total { get; set; }
        public bool CanDownload { get; set; }
    }
    internal sealed class UpgradeSession
    {
        private readonly string kind, directory;
        private readonly Func<CancellationToken, UpdateRelease> checkRelease, repairRelease;
        private readonly Func<IntegrityResult> checkFiles;
        private readonly Func<UpdateRelease, CancellationToken, Action<long>, string> prepare;
        private readonly Action<string, UpdateRelease, bool> install;
        private UpdateRelease release;
        private string payload;
        internal UpgradeResult State { get; private set; }
        internal UpgradeSession(string kind, string directory, Func<CancellationToken, UpdateRelease> checkRelease,
            Func<CancellationToken, UpdateRelease> repairRelease, Func<IntegrityResult> checkFiles,
            Func<UpdateRelease, CancellationToken, Action<long>, string> prepare, Action<string, UpdateRelease, bool> install)
        {
            if (kind != "update" && kind != "repair") throw new InvalidOperationException("维护请求无效。");
            this.kind = kind; this.directory = directory; this.checkRelease = checkRelease; this.repairRelease = repairRelease;
            this.checkFiles = checkFiles; this.prepare = prepare; this.install = install;
            State = Result("starting", "正在准备维护任务。");
        }
        private UpgradeResult Result(string status, string message, long bytes = 0)
        { return new UpgradeResult { Kind = kind, Status = status, Message = message, Version = release == null ? null : release.Tag, Notes = release == null ? null : release.Notes, Bytes = bytes, Total = release == null ? 0 : release.Size }; }
        internal UpgradeResult Handle(string action, CancellationToken cancel, Action<UpgradeResult> report)
        {
            cancel.ThrowIfCancellationRequested();
            if (action == "check" && State.Status == "starting") {
                report(Result("checking", kind == "update" ? "正在检查版本…" : "正在检查程序文件…"));
                if (kind == "repair") {
                    var files = checkFiles();
                    State = Result(files.Passed ? "passed" : "repairable", files.Passed ? "文件检查通过，无需修复。" : "发现文件缺损，可下载当前版本完整包后确认修复。");
                    State.Notes = files.Message;
                    State.Directory = files.Directory;
                    State.Entries = files.Entries.ToArray();
                } else {
                    release = checkRelease(cancel);
                    State = Result(release == null ? "unavailable" : UpdatePackage.CanInstall(release.Version, release.PackageFormat, directory) ? "available" : "current", release == null ? "暂未找到可用版本。" : UpdatePackage.CanInstall(release.Version, release.PackageFormat, directory) ? "发现可安装版本。" : "当前没有需要安装的版本。");
                }
                cancel.ThrowIfCancellationRequested();
                if (State.Status == "available" || State.Status == "repairable") {
                    if (PanelLaunch.IsCandidate(directory)) { State.Status = "unavailable"; State.Message = PanelLaunch.LegacyUpdateBlocked; }
                    else { if (release != null) UpdatePackage.RequireTarget(directory, release.PackageFormat); State.CanDownload = true; }
                }
            } else if (action == "download" && State.CanDownload) {
                State.CanDownload = false;
                State = Result("downloading", "正在准备下载，尚未替换程序文件。"); report(State);
                if (kind == "repair") release = repairRelease(cancel);
                if (release == null) throw new InvalidDataException("没有可下载的正式包。");
                UpdatePackage.RequireTarget(directory, release.PackageFormat);
                payload = prepare(release, cancel, bytes => report(Result("downloading", "正在下载并校验，尚未替换程序文件。", bytes)));
                cancel.ThrowIfCancellationRequested();
                State = Result("ready", "下载与校验完成。确认后将关闭声间，备份并替换程序文件，再重新启动。");
            } else if (action == "install" && State.Status == "ready") {
                State = Result("installing", "正在准备退出和安装，不能重复提交，请等待。"); report(State);
                install(payload, release, kind == "repair");
                State = Result("handoff", "已交给安装助手，正在退出并重启。尚未确认安装完成。");
            } else throw new InvalidOperationException("当前步骤不能执行此操作，请重新检查。");
            return State;
        }
    }
    internal static class PanelUpgrade
    {
        private static string ReadCommand()
        {
            var text = new StringBuilder();
            for (;;) {
                int ch = Console.In.Read();
                if (ch < 0) return text.Length == 0 ? null : text.ToString();
                if (ch == '\n') return text.ToString().TrimEnd('\r');
                if (text.Length >= 32) throw new InvalidDataException("维护请求过长。");
                text.Append((char)ch);
            }
        }
        internal static void Main(Func<string, UpgradeSession> factory = null)
        {
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            string kind = "update"; bool committing = false;
            var gate = new object();
            using (var cancel = new CancellationTokenSource())
            using (var commands = new BlockingCollection<string>(4))
            {
                Action<UpgradeResult> report = value => Console.WriteLine(Wire.Encode(value));
                try {
                    var first = Task.Run(() => ReadCommand());
                    if (!first.Wait(3000)) throw new IOException("维护请求超时。");
                    kind = first.Result;
                    var session = factory == null ? new UpgradeSession(kind, AppDomain.CurrentDomain.BaseDirectory, AppUpdate.Check,
                        token => AppUpdate.RepairRelease(token), () => FileIntegrity.Check(AppDomain.CurrentDomain.BaseDirectory), AppUpdate.Prepare,
                        (payload, release, repair) => { if (repair) UpdateInstaller.LaunchRepair(payload, Process.GetCurrentProcess().Id, release.PackageFormat); else UpdateInstaller.Launch(payload, release, Process.GetCurrentProcess().Id); }) : factory(kind);
                    // Reader remains independent of downloads; EOF/owner exit is cancellation before install.
                    Task.Run(() => {
                        try {
                            string line;
                            while ((line = ReadCommand()) != null && line != "cancel") {
                                if (line.Length > 32 || !commands.TryAdd(line)) break;
                            }
                        } catch { }
                        finally { lock (gate) { if (!committing) try { cancel.Cancel(); } catch (ObjectDisposedException) { } } }
                    });
                    cancel.CancelAfter(180000);
                    if (UpdateInstaller.IsUpdating()) throw new IOException("另一个安装任务正在进行，请完成后重试。");
                    var state = session.Handle("check", cancel.Token, report); report(state);
                    while (state.Status == "available" || state.Status == "repairable" || state.Status == "ready") {
                        cancel.CancelAfter(600000);
                        string action = commands.Take(cancel.Token);
                        lock (gate) {
                            cancel.Token.ThrowIfCancellationRequested();
                            if (action == "install" && state.Status == "ready") { committing = true; cancel.CancelAfter(Timeout.Infinite); }
                        }
                        if (!committing) cancel.CancelAfter(180000);
                        state = session.Handle(action, cancel.Token, report); report(state);
                    }
                } catch (Exception ex) {
                    try { report(new UpgradeResult { Kind = kind, Status = cancel.IsCancellationRequested && !committing ? "cancelled" : "error",
                        Message = cancel.IsCancellationRequested && !committing ? "已停止本次任务；下载缓存会保留，没有安装或重启程序。" : (committing ? "安装交接未获确认，请查看程序和安装结果，勿重复提交。" : "维护未完成：") + ex.Message });
                    } catch (IOException) { /* The owning panel may already have closed its output pipe. */ }
                }
            }
        }
    }
}
