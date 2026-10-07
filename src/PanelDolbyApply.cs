// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace AudioSwitch
{
    public sealed class PanelDolbyOperation
    {
        public string Token { get; set; }
        public string DeviceId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }
    // Owner-thread adapter. Audio writes and rollback remain in the original queue/worker.
    internal sealed class PanelDolbyApply : IDisposable
    {
        private readonly DolbyQueue queue;
        private readonly Timer watch = new Timer { Interval = 250 };
        private PanelDolbyOperation operation;
        private Process owner;
        private Action<DolbyResult, bool> completion;
        private bool disposed;
        internal PanelDolbyApply(DolbyQueue queue) { this.queue = queue; watch.Tick += delegate { CheckOwner(); }; }
        internal bool Running { get { return operation != null && (operation.Status == "running" || operation.Status == "cancelling"); } }
        internal PanelDolbyOperation Start(Preferences preferences, AudioState state, Request request, Action persist)
        {
            Guid token;
            if (disposed || request == null || !Guid.TryParseExact(request.Token, "N", out token) || request.Token == (operation == null ? null : operation.Token))
                throw new InvalidOperationException("此应用请求已失效，请重新打开编辑器。");
            if (Running || queue.Applying) throw new InvalidOperationException("Dolby 正在处理其他任务，请稍后再应用。");
            if (request.DolbyProfile == null || state.Default(0, 1) != request.DeviceId || !state.Devices.Any(d => d.Id == request.DeviceId && d.Flow == 0))
                throw new InvalidOperationException("立即应用需要启用方案，并选择当前在线输出设备。");
            // Holding the process handle also protects against PID reuse.
            if (request.OwnerPid <= 0 || request.OwnerPid == Process.GetCurrentProcess().Id) throw new InvalidOperationException("无法确认面板进程。");
            var candidate = Process.GetProcessById(request.OwnerPid);
            try
            {
                var handle = candidate.Handle;
                if (candidate.HasExited) throw new InvalidOperationException("面板已经关闭。");
                PanelProfile.SaveDolby(preferences, state, request, persist);
            }
            catch { candidate.Dispose(); throw; }
            owner = candidate;
            operation = new PanelDolbyOperation { Token = request.Token, DeviceId = request.DeviceId, Status = "running", Message = "方案已保存，正在应用 Dolby…" };
            completion = (result, cancelled) => {
                if (disposed) return;
                operation.Status = cancelled ? "cancelled" : result.Error != null ? "error" : result.Warning != null ? "warning" : "applied";
                operation.Message = cancelled ? "本次应用已停止，已保存的方案仍保留。" : result.Error != null ? "方案已保存，但应用失败。" : result.Warning != null ? "方案已保存，应用完成但有提示。" : "方案已保存，Dolby 已应用。";
                // A cancellation arriving after completion is not evidence of rollback.
                if (cancelled && result.Error == null) { operation.Status = "warning"; operation.Message = "本次应用已完成，取消或后续任务到达较晚；请检查当前音效。已保存的方案仍保留。"; }
                // Preserve rollback failures and driver warnings, including on cancellation.
                if (result.Error != null) operation.Message += result.Error;
                if (result.Warning != null) operation.Message += result.Warning;
                ReleaseOwner();
            };
            try { queue.ApplyOwned(request.DeviceId, request.DolbyProfile, completion); }
            catch { operation.Status = "error"; operation.Message = "方案已保存，但应用未开始。"; ReleaseOwner(); throw; }
            watch.Start();
            return Read(request.Token);
        }
        internal PanelDolbyOperation Read(string token)
        {
            if (operation == null || operation.Token != token) throw new InvalidOperationException("应用记录已失效，无法确认本次结果，请检查当前音效。");
            return Wire.Decode<PanelDolbyOperation>(Wire.Encode(operation));
        }
        internal PanelDolbyOperation Cancel(string token)
        {
            Read(token);
            if (Running) { queue.CancelOwned(completion); operation.Status = "cancelling"; operation.Message = "正在停止本次应用并等待恢复检查…"; }
            return Read(token);
        }
        internal void CheckOwner()
        {
            if (!Running || owner == null) return;
            try { if (!owner.HasExited) return; } catch (InvalidOperationException) { }
            Cancel(operation.Token);
            // Cancellation has reached the original worker; no need to poll a dead owner.
            watch.Stop();
        }
        private void ReleaseOwner() { watch.Stop(); if (owner != null) { owner.Dispose(); owner = null; } }
        public void Dispose() { if (disposed) return; disposed = true; if (Running) queue.CancelOwned(completion); ReleaseOwner(); watch.Dispose(); }
    }
}
