// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class AutoUpdatePreferenceTests
    {
        internal static void Run(Action<bool, string> check)
        {
            check(new Preferences().AutoUpdateEnabled && PreferenceStore.Parse("{\"GameMode\":false}").AutoUpdateEnabled, "automatic updates default on for new and old configurations");
            var preferences = new Preferences { AutoUpdateEnabled = false };
            // Cache keys add another long directory; do not inherit the nested release build path.
            string path = Path.Combine(Path.GetTempPath(), "AudioSwitch-pref-" + Guid.NewGuid().ToString("N"), "settings.json");
            PreferenceStore.Save(path, preferences);
            check(!PreferenceStore.Load(path).AutoUpdateEnabled && !Wire.Decode<Preferences>(Wire.Encode(preferences)).AutoUpdateEnabled, "disabled automatic updates survive restart and IPC");
            check(!PreferenceStore.Parse(PreferenceStore.Export(preferences)).AutoUpdateEnabled, "export and import preserve disabled automatic updates");
            foreach (string invalid in new[] { "null", "1", "\"false\"" })
            {
                bool rejected = false; try { PreferenceStore.Parse("{\"AutoUpdateEnabled\":" + invalid + "}"); } catch (InvalidOperationException) { rejected = true; }
                check(rejected, "automatic updates reject non-boolean setting: " + invalid);
            }
            string backup;
            check(PreferenceStore.Import(path, "{\"DarkMode\":false}", preferences, out backup).AutoUpdateEnabled && !PreferenceStore.Load(backup).AutoUpdateEnabled,
                "older imports default automatic updates on and preserve the previous off setting in backup");
            int checks = 0;
            using (var updates = new BackgroundUpdate(token => { checks++; return null; }, (release, token) => "unused", (release, payload) => { }, () => DateTime.UtcNow))
            {
                preferences.GameMode = true;
                updates.SetPaused(!preferences.AutomaticUpdatesAllowed, "自动更新已关闭，可手动检查更新。"); updates.Start();
                preferences.GameMode = false;
                updates.SetPaused(!preferences.AutomaticUpdatesAllowed, "自动更新已关闭，可手动检查更新。");
                check(checks == 0 && updates.Work == null && updates.Snapshot().Message.Contains("自动更新已关闭"), "leaving game mode cannot override an explicitly disabled update setting");
                preferences.GameMode = true; preferences.AutoUpdateEnabled = true;
                updates.SetPaused(!preferences.AutomaticUpdatesAllowed);
                check(updates.Work == null && updates.Snapshot().Message.Contains("游戏模式"), "enabling automatic updates during game mode keeps downloads paused and updates the reason");
                preferences.GameMode = false; updates.SetPaused(!preferences.AutomaticUpdatesAllowed);
                check(updates.Work.Wait(5000) && checks == 1, "automatic check resumes only when both settings permit it");
            }
            preferences.AutoUpdateEnabled = false;
            var store = new AutomaticUpdateStore(Path.GetDirectoryName(path), Path.Combine(Path.GetDirectoryName(path), "AudioSwitch.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(store.PathName)); File.WriteAllText(store.PathName, "unread cache");
            bool launched = false; string issue;
            check(!store.TryInstall(true, (payload, release, bg) => launched = true, out issue, !preferences.AutomaticUpdatesAllowed) && !launched && issue == null,
                "disabled automatic updates skip cached startup installation before reading its files");
        }
        private static IEnumerable<Control> Children(Control control)
        { foreach (Control child in control.Controls) { yield return child; foreach (var inner in Children(child)) yield return inner; } }
        internal static void Render(Action<bool, string> check)
        {
            foreach (bool dark in new[] { false, true })
            {
                var prefs = new Preferences { DarkMode = dark };
                var reply = new Reply { Preferences = prefs, State = new AudioState(), Pending = new List<Arrival>() };
                var requests = new List<Request>(); bool fail = false;
                Palette.Apply(dark);
                Func<Request, Task<Reply>> send = request => {
                    requests.Add(request);
                    if (request.Action == "automaticUpdates") { if (!fail) prefs.AutoUpdateEnabled = request.Value; reply.Error = fail ? "设置保存失败" : null; }
                    reply.Update = prefs.AutoUpdateEnabled ? null : new BackgroundUpdateState { Stage = "paused", Message = "自动更新已关闭，可手动检查更新。" };
                    return Task.FromResult(reply);
                };
                using (var form = new UpdateDialog(true, send))
                {
                    form.Show(); Application.DoEvents();
                    var toggle = Children(form).OfType<CheckBox>().Single(c => c.Name == "automaticUpdates");
                    check(toggle.Checked, "automatic update switch initially shows enabled in theme " + dark);
                    toggle.Checked = false; Application.DoEvents();
                    check(!prefs.AutoUpdateEnabled && requests.Last().Action == "automaticUpdates", "automatic update switch sends dedicated saved-state request in theme " + dark);
                    check(toggle.Parent == form && toggle.Parent.ClientRectangle.Contains(toggle.Bounds) && toggle.Parent.Controls.Cast<Control>().Where(c => c != toggle).All(c => !c.Bounds.IntersectsWith(toggle.Bounds)), "automatic update switch fits inside update dialog without overlap in theme " + dark);
                    check(Children(form).Single(c => c.Name == "checkUpdates").Enabled, "manual update entry remains available while automatic updates are off in theme " + dark);
                    string image = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", dark ? "automatic-switch-dark.png" : "automatic-switch-light.png"));
                    using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(image); }
                    fail = true; toggle.Checked = true; Application.DoEvents();
                    check(!toggle.Checked && !prefs.AutoUpdateEnabled, "failed save restores confirmed automatic update switch state in theme " + dark);
                    check(requests.All(r => r.Action == "snapshot" || r.Action == "automaticUpdates"), "automatic update toggle makes no audio mutation requests in theme " + dark);
                    form.Close();
                }
                using (var reopened = new UpdateDialog(true, send))
                {
                    reopened.Show(); Application.DoEvents();
                    check(!Children(reopened).OfType<CheckBox>().Single(c => c.Name == "automaticUpdates").Checked, "reopening update dialog reads saved off setting in theme " + dark);
                    reopened.Close();
                }
                using (var failedLoad = new UpdateDialog(true, request => { throw new InvalidOperationException("offline"); }))
                {
                    failedLoad.Show(); Application.DoEvents();
                    check(!Children(failedLoad).OfType<CheckBox>().Single(c => c.Name == "automaticUpdates").Enabled && Children(failedLoad).Single(c => c.Name == "checkUpdates").Enabled,
                        "unavailable preferences disable only the toggle, leaving manual updates available in theme " + dark);
                    failedLoad.Close();
                }
                using (var exiting = new UpdateDialog(true, request => Task.FromResult(new Reply { Error = "后台正在退出" })))
                {
                    exiting.Show(); Application.DoEvents();
                    check(!Children(exiting).OfType<CheckBox>().Single(c => c.Name == "automaticUpdates").Enabled, "an error-only reply cannot be mistaken for confirmed default preferences in theme " + dark);
                    exiting.Close();
                }
                var pendingSave = new TaskCompletionSource<Reply>();
                using (var saving = new UpdateDialog(true, request => request.Action == "snapshot" ? Task.FromResult(reply) : pendingSave.Task))
                {
                    saving.Show(); Application.DoEvents();
                    var toggle = Children(saving).OfType<CheckBox>().Single(c => c.Name == "automaticUpdates");
                    toggle.Checked = true; Application.DoEvents();
                    check(!toggle.Enabled && !Children(saving).Single(c => c.Name == "checkUpdates").Enabled, "pending preference save blocks duplicate edits and manual install in theme " + dark);
                    pendingSave.SetException(new InvalidOperationException("connection lost")); Application.DoEvents();
                    check(!toggle.Checked && toggle.Enabled && Children(saving).Single(c => c.Name == "checkUpdates").Enabled, "transport failure restores confirmed setting and manual action in theme " + dark);
                    saving.Close();
                }
            }
        }
    }
}
