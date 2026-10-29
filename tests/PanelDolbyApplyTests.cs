using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace AudioSwitch
{
    internal static class PanelDolbyApplyTests
    {
        internal static void Run(Action<bool, string> check)
        {
            foreach (string scenario in new[] { "success", "error", "warning", "cancel", "superseded", "owner", "late", "empty" })
            {
                var callbacks = new ConcurrentQueue<Action>();
                using (var started = new ManualResetEvent(false))
                using (var release = new ManualResetEvent(false))
                using (var owner = Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--dolby-owner-fixture") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true }))
                {
                    CancellationToken active = CancellationToken.None; int runs = 0;
                    var queue = new DolbyQueue(a => callbacks.Enqueue(a), (id, p, token) => {
                        Interlocked.Increment(ref runs);
                        if (id == "current") { active = token; started.Set(); release.WaitOne(3000); }
                        if (scenario == "empty") return null;
                        return new DolbyResult { Error = scenario == "error" || scenario == "cancel" ? "写入失败；恢复未通过核验。" : token.IsCancellationRequested ? "任务已取消，本次更改已恢复。" : null, Warning = scenario == "warning" ? "驱动调整了强度。" : null };
                    }, (id, result) => { });
                    using (var panel = new PanelDolbyApply(queue))
                    try
                    {
                        var preferences = new Preferences();
                        preferences.DeviceProfiles["current"] = new DeviceProfile { Volume = 37, SpatialFormat = "" };
                        var state = new AudioState(); state.Devices.Add(new Endpoint { Id = "current", Flow = 0 }); state.Defaults["0:1"] = "current";
                        var request = new Request { DeviceId = "current", Token = Guid.NewGuid().ToString("N"), OwnerPid = owner.Id, DolbyProfile = new DolbyProfile { Enabled = false } };
                        int saves = 0;
                        request.DeviceId = "other"; Reject(() => panel.Start(preferences, state, request, () => saves++), check, "apply rejects noncurrent output before save"); request.DeviceId = "current";
                        Reject(() => panel.Start(preferences, state, request, () => { throw new InvalidOperationException("disk"); }), check, "failed save starts no worker");
                        check(runs == 0 && preferences.DeviceProfiles["current"].Dolby == null, "failed apply-save restores memory without writes");
                        panel.Start(preferences, state, request, () => saves++);
                        check(started.WaitOne(1500) && saves == 1 && panel.Read(request.Token).Status == "running", "saved does not mean Dolby apply completed");
                        check(preferences.DeviceProfiles["current"].Volume == 37 && preferences.DeviceProfiles["current"].SpatialFormat == "" && state.Default(0, 1) == "current", "Dolby apply adapter preserves basic profile and routing");
                        Reject(() => panel.Start(preferences, state, request, () => saves++), check, "duplicate apply cannot save or enqueue twice");
                        Reject(() => panel.Cancel("stale"), check, "stale cancel cannot affect current task");
                        if (scenario == "cancel") { panel.Cancel(request.Token); check(active.IsCancellationRequested && panel.Running, "cancel waits for worker restore before terminal result"); }
                        if (scenario == "superseded") { queue.Observe("next", new DolbyProfile { Enabled = true }); panel.Cancel(request.Token); }
                        if (scenario == "owner") {
                            owner.StandardInput.Close(); check(owner.WaitForExit(1500), "isolated UI owner exits");
                            var ownerWait = Stopwatch.StartNew();
                            while (!active.IsCancellationRequested && ownerWait.ElapsedMilliseconds < 1500) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
                            check(active.IsCancellationRequested && panel.Running, "real owner timer signals cancellation while worker restores");
                        }
                        release.Set();
                        if (scenario == "late") { var wait = Stopwatch.StartNew(); while (callbacks.IsEmpty && wait.ElapsedMilliseconds < 1500) Thread.Sleep(5); panel.Cancel(request.Token); }
                        Drain(callbacks, () => !queue.Applying);
                        var result = panel.Read(request.Token);
                        string expected = scenario == "success" ? "applied" : scenario == "error" || scenario == "empty" ? "error" : scenario == "warning" || scenario == "late" ? "warning" : "cancelled";
                        check(!panel.Running && result.Status == expected, "manual Dolby result: " + scenario);
                        if (scenario == "cancel" || scenario == "error") check(result.Message.Contains("恢复未通过核验"), "rollback failure remains visible");
                        if (scenario == "warning") check(result.Message.Contains("驱动调整"), "driver warning is not hidden as success");
                        if (scenario == "late") check(result.Message.Contains("到达较晚") && !result.Message.Contains("已恢复"), "late cancellation cannot claim completed writes were restored");
                        if (scenario == "superseded") check(runs == 2, "editor cancellation preserves newer automatic queued work");
                        check(panel.Cancel(request.Token).Status == expected, "late cancel cannot turn a completed result into cancelled");
                    }
                    finally { queue.Stop(); release.Set(); Drain(callbacks, () => callbacks.IsEmpty); if (!owner.HasExited) { owner.StandardInput.Close(); owner.WaitForExit(1500); } }
                }
            }
        }
        private static void Drain(ConcurrentQueue<Action> callbacks, Func<bool> done)
        {
            var watch = Stopwatch.StartNew();
            do { Action action; while (callbacks.TryDequeue(out action)) action(); Thread.Sleep(5); } while (!done() && watch.ElapsedMilliseconds < 3000);
        }
        private static void Reject(Action action, Action<bool, string> check, string message)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } check(rejected, message); }
    }
}
