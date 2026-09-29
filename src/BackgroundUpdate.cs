// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AudioSwitch
{
    public sealed class BackgroundUpdateState
    {
        public string Stage { get; set; }
        public string Version { get; set; }
        public string Message { get; set; }
    }
    internal sealed class BackgroundUpdate : IDisposable
    {
        private readonly object gate = new object();
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Func<CancellationToken, UpdateRelease> check;
        private readonly Func<UpdateRelease, CancellationToken, string> prepare;
        private readonly Action<UpdateRelease, string> stage;
        private readonly Func<DateTime> now;
        private readonly Version blocked;
        private BackgroundUpdateState state = new BackgroundUpdateState();
        private UpdateRelease release;
        private Task work;
        private CancellationTokenSource download;
        private CancellationTokenSource checking;
        private bool paused, checkedOnce, checkSuspended;
        private string pauseReason;
        private bool yielded;
        private bool disposed;
        private int attempts;
        private DateTime retryAfter;
        internal BackgroundUpdate(AutomaticUpdateStore store, string issue) : this(AppUpdate.Check,
            store.Prepare, store.Stage, () => DateTime.UtcNow, BlockedVersion(store), issue) { }
        private static Version BlockedVersion(AutomaticUpdateStore store)
        {
            try { var item = store.Read(); return item != null && item.Attempted ? AppUpdate.ParseVersion(item.Version) : null; }
            catch { return null; }
        }
        internal BackgroundUpdate(Func<CancellationToken, UpdateRelease> check, Func<UpdateRelease, CancellationToken, string> prepare,
            Action<UpdateRelease, string> stage, Func<DateTime> now, Version blocked = null, string issue = null)
        {
            this.check = check; this.prepare = prepare; this.stage = stage; this.now = now; this.blocked = blocked;
            if (issue != null) state = new BackgroundUpdateState { Stage = "error", Message = issue };
        }
        internal BackgroundUpdateState Snapshot()
        {
            lock (gate) return paused ? new BackgroundUpdateState { Stage = "paused", Version = state.Version,
                Message = pauseReason != null ? pauseReason + (state.Stage == "ready" ? "已下载的更新会保留。" : "") : state.Stage == "ready" ? "游戏模式已开启，已下载更新保留，关闭后下次启动安装。" : "游戏模式已开启，自动更新和负载检测已暂停。" } :
                new BackgroundUpdateState { Stage = state.Stage, Version = state.Version, Message = state.Message };
        }
        internal bool Finished { get { lock (gate) return !paused && checkedOnce && work != null && work.IsCompleted && state.Stage != "waiting" && state.Stage != "downloading"; } }
        internal Task Work { get { lock (gate) return work; } }
        internal bool NeedsActivity { get { lock (gate) return !disposed && !paused && (state.Stage == "downloading" || (state.Stage == "waiting" && now() >= retryAfter)); } }
        internal void SetPaused(bool value, string reason = null)
        {
            lock (gate)
            {
                if (disposed) return;
                pauseReason = reason;
                if (paused == value) return;
                paused = value;
                if (value)
                {
                    if (checking != null) { checkSuspended = true; checking.Cancel(); }
                    if (download != null) { yielded = true; download.Cancel(); }
                }
                else Start();
            }
        }
        internal void Start()
        {
            lock (gate)
            {
                if (disposed || paused || checkedOnce || (work != null && !work.IsCompleted)) return;
                checkSuspended = false;
                checking = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var timeout = checking; timeout.CancelAfter(30000);
                work = Task.Run(() => {
                    try
                    {
                        UpdateRelease found;
                        found = check(timeout.Token); timeout.Token.ThrowIfCancellationRequested();
                        lock (gate)
                        {
                            if (disposed || checkSuspended) return;
                            checkedOnce = true;
                            if (found == null || found.Version <= AppUpdate.ParseVersion(AppVersion.Number)) return;
                            release = found;
                            if (blocked != null && found.Version <= blocked)
                                state = new BackgroundUpdateState { Stage = "error", Version = found.Tag, Message = "上次自动安装未完成，请点击检查更新手动重试。" };
                            else state = new BackgroundUpdateState { Stage = "waiting", Version = found.Tag, Message = "发现 " + found.Tag + "，将在空闲时下载，下次启动时安装。" };
                        }
                    }
                    catch (Exception ex) { lock (gate) if (!disposed && !checkSuspended) { checkedOnce = true; state = new BackgroundUpdateState { Stage = "error", Message = "自动检查未完成，可点击检查更新重试。" + ex.Message }; } }
                    finally { lock (gate) { checking = null; timeout.Dispose(); } }
                });
            }
        }
        internal void Tick(bool idle, bool busy)
        {
            lock (gate)
            {
                if (disposed || paused) return;
                if (busy && state.Stage == "downloading" && download != null)
                { yielded = true; download.Cancel(); return; }
                if (!idle || busy || work == null || !work.IsCompleted || state.Stage != "waiting" || now() < retryAfter) return;
                state = new BackgroundUpdateState { Stage = "downloading", Version = release.Tag, Message = "正在后台下载 " + release.Tag + "，本次使用不会重启。" };
                attempts++;
                yielded = false;
                download = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var timeout = download; timeout.CancelAfter(180000);
                work = Task.Run(() => {
                    try
                    {
                        string payload;
                        payload = prepare(release, timeout.Token); timeout.Token.ThrowIfCancellationRequested();
                        lock (gate)
                        {
                            if (disposed) return;
                            timeout.Token.ThrowIfCancellationRequested();
                            stage(release, payload);
                            state = new BackgroundUpdateState { Stage = "ready", Version = release.Tag, Message = release.Tag + " 已下载，下次启动时自动安装。" };
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (gate) if (!disposed)
                        {
                            if (yielded)
                            {
                                attempts--; retryAfter = now().AddSeconds(30);
                                state = new BackgroundUpdateState { Stage = "waiting", Version = release.Tag, Message = "下载已暂缓，等待全屏应用退出且系统空闲后继续。" };
                            }
                            else
                            {
                                retryAfter = now().AddMinutes(5);
                                bool retry = attempts < 3 && !(ex is UpdateStorageException);
                                state = new BackgroundUpdateState { Stage = retry ? "waiting" : "error", Version = release.Tag,
                                    Message = retry ? "下载暂未完成，将在稍后空闲时重试。" : "自动下载未完成，请点击检查更新重试。" + ex.Message };
                            }
                        }
                    }
                    finally { lock (gate) { download = null; timeout.Dispose(); } }
                });
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true; stop.Cancel();
                if (work == null || work.IsCompleted) stop.Dispose();
                else work.ContinueWith(task => stop.Dispose(), TaskScheduler.Default);
            }
        }
    }
    internal static class UpdateIdle
    {
        [StructLayout(LayoutKind.Sequential)] private struct LastInput { internal uint Size, Time; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
        internal static bool IsIdle()
        {
            var input = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
            return GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.Time) >= 120000;
        }
    }
}
