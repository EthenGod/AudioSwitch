// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class GameModeTests
    {
        private static void Wait(BackgroundUpdate updater)
        { if (!updater.Work.Wait(5000)) throw new Exception("Game mode worker did not complete."); }
        internal static void Run(Action<bool, string> check)
        {
            check(!PreferenceStore.Parse("{\"DarkMode\":false}").GameMode, "old configuration defaults game mode off");
            var preferences = new Preferences { GameMode = true };
            check(PreferenceStore.Parse(PreferenceStore.Export(preferences)).GameMode && Wire.Decode<Preferences>(Wire.Encode(preferences)).GameMode, "game mode survives exported configuration and IPC");
            foreach (string invalid in new[] { "1", "null", "\"true\"" })
            {
                bool rejected = false; try { PreferenceStore.Parse("{\"GameMode\":" + invalid + "}"); } catch (InvalidOperationException) { rejected = true; }
                check(rejected, "game mode rejects non-boolean configuration: " + invalid);
            }
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game-mode-tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "settings.json"); PreferenceStore.Save(path, preferences);
            check(PreferenceStore.Load(path).GameMode, "game mode is remembered after a settings reload");
            string backup; var imported = PreferenceStore.Import(path, "{\"DarkMode\":false}", preferences, out backup);
            check(!imported.GameMode && PreferenceStore.Load(backup).GameMode, "importing older settings restores default and preserves previous game mode in backup");
            var store = new AutomaticUpdateStore(root, Path.Combine(root, "AudioSwitch.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(store.PathName)); File.WriteAllText(store.PathName, "broken cache record");
            bool launched = false; string issue;
            check(!store.TryInstall(true, (payload, found, bg) => launched = true, out issue, true) && !launched && issue == null && File.ReadAllText(store.PathName) == "broken cache record",
                "persisted game mode skips cached startup installation and leaves its record untouched");
            int checks = 0, downloads = 0, stages = 0;
            var release = new UpdateRelease { Version = new Version(99, 0, 0), Tag = "v99.0.0" };
            using (var updater = new BackgroundUpdate(token => { checks++; return release; },
                (found, token) => { downloads++; return "payload"; }, (found, payload) => stages++, () => DateTime.UtcNow))
            {
                updater.SetPaused(true); updater.Start(); updater.Tick(true, false);
                check(updater.Work == null && !updater.NeedsActivity && checks == 0 && updater.Snapshot().Stage == "paused", "saved game mode prevents startup network checks and idle sampling");
                updater.SetPaused(false); Wait(updater);
                check(checks == 1 && updater.NeedsActivity, "disabling game mode starts deferred check once");
                updater.SetPaused(true); updater.Tick(true, false);
                check(downloads == 0 && !updater.NeedsActivity, "game mode prevents an already discovered release from downloading");
                updater.SetPaused(false); updater.Tick(true, false); Wait(updater);
                updater.SetPaused(true);
                check(stages == 1 && updater.Snapshot().Message.Contains("已下载更新保留"), "game mode retains a completed staged update without installing it");
                updater.SetPaused(false); updater.Start(); updater.Tick(true, false);
                check(checks == 1 && downloads == 1 && updater.Snapshot().Stage == "ready", "resume preserves ready cache without rechecking or redownloading");
            }
            checks = 0;
            using (var entered = new ManualResetEvent(false))
            using (var updater = new BackgroundUpdate(token => {
                checks++; if (checks == 1) { entered.Set(); token.WaitHandle.WaitOne(5000); token.ThrowIfCancellationRequested(); } return release;
            }, (found, token) => "payload", (found, payload) => { }, () => DateTime.UtcNow))
            {
                updater.Start(); check(entered.WaitOne(5000), "network check begins before game mode is enabled");
                updater.SetPaused(true); updater.SetPaused(false); Wait(updater); updater.Start(); Wait(updater);
                check(checks == 2 && updater.Snapshot().Stage == "waiting", "rapid game mode toggles cancel then resume check without a false network error");
            }
            downloads = 0; stages = 0; var clock = DateTime.UtcNow;
            using (var entered = new ManualResetEvent(false))
            using (var updater = new BackgroundUpdate(token => release, (found, token) => {
                downloads++; if (downloads == 1) { entered.Set(); token.WaitHandle.WaitOne(5000); token.ThrowIfCancellationRequested(); } return "payload";
            }, (found, payload) => stages++, () => clock))
            {
                updater.Start(); Wait(updater); updater.Tick(true, false); check(entered.WaitOne(5000), "download begins before game mode is enabled");
                updater.SetPaused(true); Wait(updater);
                check(stages == 0 && !updater.NeedsActivity && updater.Snapshot().Stage == "paused", "enabling game mode cancels in-flight download and blocks installation staging");
                clock = clock.AddMinutes(1); updater.SetPaused(false); updater.Tick(true, false); Wait(updater);
                check(downloads == 2 && stages == 1, "disabling game mode resumes eligible idle download");
            }
        }
        private static IEnumerable<Control> Children(Control control)
        { foreach (Control child in control.Controls) { yield return child; foreach (var inner in Children(child)) yield return inner; } }
        private static void Pump(int milliseconds)
        { var watch = System.Diagnostics.Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
        internal static void Render(Action<bool, string> check)
        {
            int events = 0; long first = -1; var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var batch = new DeviceEventBatch(() => { events++; if (first < 0) first = watch.ElapsedMilliseconds; }))
            {
                batch.SetGameMode(true); batch.Signal();
                while (watch.ElapsedMilliseconds < 450) { Application.DoEvents(); batch.Signal(); Thread.Sleep(10); }
                check(events == 0, "game mode coalesces repeated device notifications for 500 ms");
                while (watch.ElapsedMilliseconds < 680) { Application.DoEvents(); Thread.Sleep(10); }
                check(events == 1 && first < 680, "continuous device events cannot postpone the game mode batch indefinitely");
                batch.SetGameMode(false); watch.Restart(); batch.Signal();
                while (watch.ElapsedMilliseconds < 300) { Application.DoEvents(); Thread.Sleep(10); }
                check(events == 2, "leaving game mode restores normal event response time");
                while (watch.ElapsedMilliseconds < 500) { Application.DoEvents(); Thread.Sleep(10); }
                check(events == 2, "game mode event batching adds no idle polling");
            }
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            foreach (bool dark in new[] { false, true })
            {
                var prefs = new Preferences { DarkMode = dark };
                var reply = new Reply { Preferences = prefs, State = new AudioState(), Pending = new List<Arrival>() };
                var requests = new List<Request>(); bool fail = false;
                using (var form = new Dashboard(false, false, request => {
                    requests.Add(request);
                    if (request.Action == "gameMode") { if (!fail) prefs.GameMode = request.Value; reply.Error = fail ? "游戏模式保存失败" : null; }
                    reply.Update = prefs.GameMode ? new BackgroundUpdateState { Stage = "paused", Message = "游戏模式已开启，自动更新和负载检测已暂停。" } : null;
                    return Task.FromResult(reply);
                }))
                {
                    form.Show(); Application.DoEvents(); form.Size = form.MinimumSize;
                    var toggle = Children(form).OfType<CheckBox>().Single(c => c.Name == "gameMode");
                    toggle.Checked = true; Application.DoEvents();
                    check(prefs.GameMode && toggle.Checked && requests.Last().Action == "gameMode", "dashboard game mode uses one dedicated backend request in theme " + dark);
                    if (!dark)
                    {
                        int snapshots = requests.Count(r => r.Action == "snapshot"); Pump(1800);
                        check(requests.Count(r => r.Action == "snapshot") == snapshots, "game mode avoids normal 1.5-second dashboard polling");
                    }
                    check(toggle.Parent.ClientRectangle.Contains(toggle.Bounds) && toggle.Parent.Controls.Cast<Control>().Where(c => c != toggle).All(c => !c.Bounds.IntersectsWith(toggle.Bounds)), "game mode fits compact header without overlapping filters in theme " + dark);
                    using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, dark ? "game-mode-dark.png" : "game-mode-light.png")); }
                    fail = true; toggle.Checked = false; Application.DoEvents();
                    check(toggle.Checked && prefs.GameMode, "failed game mode save restores confirmed toggle in theme " + dark);
                    prefs.GameMode = false; reply.Error = null; form.RenderReply(reply);
                    check(!toggle.Checked, "dashboard reflects a mode change from the tray in theme " + dark);
                    check(requests.All(r => r.Action == "snapshot" || r.Action == "gameMode"), "game mode UI does not request audio mutations in theme " + dark);
                    form.Close();
                }
                Palette.Apply(dark); int commands = 0; bool requested = false;
                using (var menu = new ThemedMenu())
                {
                    menu.Items.Add(new TrayMenuHeading());
                    menu.Items.Add("打开管理面板");
                    var toggle = GameModeMenu.Create(true, value => { commands++; requested = value; }); menu.Items.Add(toggle);
                    menu.Items.Add("退出"); menu.Show(new Point(50, 50)); Application.DoEvents();
                    check(toggle.Checked && commands == 0, "opening tray menu reflects game mode without triggering changes in theme " + dark);
                    using (var bitmap = new Bitmap(menu.Width, menu.Height)) { menu.DrawToBitmap(bitmap, menu.ClientRectangle); bitmap.Save(Path.Combine(output, dark ? "game-mode-tray-dark.png" : "game-mode-tray-light.png")); }
                    toggle.PerformClick(); check(commands == 1 && !requested, "tray game mode toggles the current state exactly once in theme " + dark);
                    menu.Close();
                }
            }
            var endpoint = new Endpoint { Id = "game-headset", Name = "游戏耳机", Flow = 0 };
            var pendingReply = new Reply { Preferences = new Preferences { GameMode = true }, State = new AudioState { Devices = new List<Endpoint> { endpoint } },
                Pending = new List<Arrival> { new Arrival { Token = "game-arrival", Flow = 0, NewDevices = new List<Endpoint> { endpoint }, PreviousDefaults = new Dictionary<string, string>() } } };
            int promptRequests = 0;
            using (var prompt = new DevicePrompt(request => {
                promptRequests++;
                if (request.Action != "snapshot") pendingReply.Pending.Clear();
                return Task.FromResult(pendingReply);
            }, false, pendingReply))
            {
                prompt.Show(); Pump(850);
                check(promptRequests == 0, "game mode reduces prompt polling below its normal 300-ms frequency");
                Children(prompt).OfType<Button>().Single(button => button.Name == "choose").PerformClick();
                check(promptRequests == 1 && prompt.IsDisposed, "manual prompt action remains immediate despite slower game-mode polling");
            }
        }
    }
}
