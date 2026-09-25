// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AudioSwitch
{
    public sealed class DolbyRequest
    {
        public string DeviceId { get; set; }
        public DolbyProfile Profile { get; set; }
        public string CancellationEvent { get; set; }
        public int OwnerPid { get; set; }
    }
    public sealed class DolbyResult { public string Error { get; set; } public string Warning { get; set; } public DolbyProfile Profile { get; set; } }
    internal static class DolbyWorker
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
        internal static DolbyResult Run(string id, DolbyProfile profile)
        { return Run(id, profile, CancellationToken.None); }
        internal static DolbyResult Run(string id, DolbyProfile profile, CancellationToken cancellation)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                string eventName = "Local\\AudioSwitch-Dolby-Cancel-" + Wire.Identity + "-" + Guid.NewGuid().ToString("N");
                using (var signal = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
                using (cancellation.Register(() => signal.Set()))
                {
                    var start = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe"), "--dolby-worker") {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                        StandardOutputEncoding = new System.Text.UTF8Encoding(false) };
                    using (var process = Process.Start(start))
                    {
                        var output = process.StandardOutput.ReadToEndAsync();
                        process.StandardInput.WriteLine(Wire.Encode(new DolbyRequest { DeviceId = id, Profile = profile, CancellationEvent = eventName, OwnerPid = Process.GetCurrentProcess().Id })); process.StandardInput.Close();
                        if (!process.WaitForExit(12000)) { try { process.Kill(); process.WaitForExit(1500); } catch { } return new DolbyResult { Error = "Dolby 响应超时，请检查当前音效；已保存的方案仍保留。" }; }
                        if (!output.Wait(1000) || String.IsNullOrWhiteSpace(output.Result)) return new DolbyResult { Error = "Dolby 辅助进程异常退出，可能有部分设置未完成，请检查当前音效。" };
                        return Wire.Decode<DolbyResult>(output.Result);
                    }
                }
            }
            catch (Exception ex) { return new DolbyResult { Error = "Dolby 操作失败：" + ex.Message }; }
        }
        internal static void Main()
        {
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
            // A winexe with redirected handles has no console code page to change.
            // Wrap the streams explicitly instead of setting Console.Input/OutputEncoding.
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            DolbyResult result;
            try
            {
                // No WinForms initialization or visible window in this short-lived process.
                var request = Wire.Decode<DolbyRequest>(Console.ReadLine());
                DolbyProfiles.Validate(request.Profile);
                using (var mutex = new Mutex(false, "Local\\AudioSwitch-Dolby-" + Wire.Identity))
                {
                    bool locked = false;
                    try
                    {
                        try { locked = mutex.WaitOne(1500); } catch (AbandonedMutexException) { locked = true; }
                        if (!locked) throw new InvalidOperationException("Dolby 正在应用其他设置，请稍后重试。");
                        using (var audio = new AudioService())
                        using (var signal = request.CancellationEvent == null ? null : EventWaitHandle.OpenExisting(request.CancellationEvent))
                        using (var owner = request.OwnerPid == 0 ? null : Process.GetProcessById(request.OwnerPid))
                        {
                            Action outputGuard = () => { if (!String.Equals(audio.Read().Default(0, 1), request.DeviceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("此设备已不是当前输出，请先选择此设备再读取或应用 Dolby。"); };
                            Action guard = () => { if ((signal != null && signal.WaitOne(0)) || (owner != null && owner.HasExited)) throw new OperationCanceledException("Dolby 任务已取消。"); outputGuard(); };
                            guard();
                            using (var native = new DolbyNative(request.DeviceId))
                            {
                                result = new DolbyResult();
                                // Cancellation stops forward writes but permits restoring this same output.
                                if (request.Profile != null) result.Warning = DolbyProfiles.Apply(request.Profile, native, guard, outputGuard);
                                else { result.Profile = DolbyProfiles.Capture(native); guard(); }
                            }
                        }
                    }
                    finally { if (locked) mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex) { result = new DolbyResult { Error = ex.Message }; }
            Console.WriteLine(Wire.Encode(result));
        }
    }
    // Owner-thread scheduling: one job in flight, replace pending work with the latest output.
    internal sealed class DolbyQueue
    {
        private readonly Action<Action> dispatch;
        private readonly Func<string, DolbyProfile, CancellationToken, DolbyResult> run;
        private readonly Action<string, DolbyResult> complete;
        private string observed;
        private DolbyRequest pending;
        private long revision;
        private bool running, stopped;
        private CancellationTokenSource activeCancellation;
        internal bool Applying { get { return !stopped && (running || pending != null); } }
        internal DolbyQueue(Action<Action> dispatch, Func<string, DolbyProfile, CancellationToken, DolbyResult> run, Action<string, DolbyResult> complete)
        { this.dispatch = dispatch; this.run = run; this.complete = complete; }
        internal void Observe(string id, DolbyProfile profile, bool force = false)
        {
            if (stopped || (!force && observed == id)) return;
            observed = id; revision++;
            if (activeCancellation != null) activeCancellation.Cancel();
            pending = profile == null || id == null ? null : new DolbyRequest { DeviceId = id, Profile = Wire.Decode<DolbyProfile>(Wire.Encode(profile)) };
            Start();
        }
        internal void Cancel() { revision++; pending = null; if (activeCancellation != null) activeCancellation.Cancel(); }
        internal void Stop() { if (stopped) return; stopped = true; Cancel(); }
        private void Start()
        {
            if (running || pending == null || stopped) return;
            var job = pending; long version = revision; pending = null; running = true;
            var cancellation = new CancellationTokenSource(); activeCancellation = cancellation;
            Task.Run(() => run(job.DeviceId, job.Profile, cancellation.Token)).ContinueWith(task => {
                try { dispatch(() => {
                    running = false; activeCancellation = null; cancellation.Dispose();
                    if (!stopped && revision == version) complete(job.DeviceId, task.IsFaulted ? new DolbyResult { Error = "Dolby 应用失败。" } : task.Result);
                    Start();
                }); } catch (InvalidOperationException) { cancellation.Dispose(); }
            });
        }
    }
}
