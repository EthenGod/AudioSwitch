// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class StartupTests
    {
        private sealed class Store : IStartupStore
        {
            internal string Value;
            internal int Writes;
            internal bool FailRead, FailAfterWrite, IgnoreWrites, FailRecovery;
            internal int Reads;
            internal HashSet<int> FailedReads = new HashSet<int>();
            internal Action<Store> AfterWrite;
            public string Read() { Reads++; if (FailRead || FailedReads.Contains(Reads)) throw new IOException("read denied"); return Value; }
            public void Write(string value)
            {
                Writes++;
                if (FailRecovery && Writes > 1) throw new IOException("recovery denied");
                if (!IgnoreWrites) Value = value;
                if (AfterWrite != null) AfterWrite(this);
                if (FailAfterWrite && Writes == 1) throw new IOException("partial write");
            }
        }
        internal static void Run(Action<bool, string> check)
        {
            string exe = @"C:\应用 空格 & (测试)\AudioSwitch.exe";
            string command = StartupRegistration.Command(exe);
            check(command == "\"" + exe + "\" --background", "startup command quotes full path and requests tray-only background mode");
            var store = new Store();
            check(!StartupRegistration.Read(exe, store, p => true).Enabled && store.Writes == 0, "absent startup entry defaults off without writes");
            StartupRegistration.Set(true, exe, store, p => true);
            check(StartupRegistration.Read(exe, store, p => true).Enabled && store.Writes == 1, "enable startup writes and verifies exact command");
            StartupRegistration.Set(true, exe, store, p => true);
            check(store.Writes == 1, "enabling startup repeatedly is idempotent");
            StartupRegistration.Set(false, exe, store, p => true);
            check(store.Value == null && !StartupRegistration.Read(exe, store, p => true).Enabled, "disable removes only this application's startup registration");
            var roundtrip = Wire.Decode<Reply>(Wire.Encode(new Reply { Startup = new StartupState { Enabled = true, Available = true } }));
            check(roundtrip.Startup.Enabled && roundtrip.Startup.Available, "startup state survives IPC serialization");
            check(!PreferenceStore.Export(new Preferences()).Contains("Startup"), "audio backup cannot enable or disable Windows startup");
            store = new Store { Value = StartupRegistration.Command(@"C:\old\AudioSwitch.exe") };
            check(!StartupRegistration.Read(exe, store, p => true).Enabled && StartupRegistration.Read(exe, store, p => true).Available, "another installation is not reported as this installation's startup");
            string failure = null;
            try { StartupRegistration.Set(false, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && store.Writes == 0, "disable never removes another installation's startup");
            StartupRegistration.Set(true, exe, store, p => true);
            check(store.Value == command, "explicit enabling updates an old installation path");
            foreach (string invalid in new[] { "unknown.exe", "\" --background", "\"C:\\other.exe\" --background" })
            {
                store = new Store { Value = invalid }; failure = null;
                try { StartupRegistration.Set(true, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
                check(failure != null && store.Writes == 0 && !StartupRegistration.Read(exe, store, p => true).Available, "unrecognized same-name startup item is preserved");
            }
            store = new Store { FailRead = true };
            check(!StartupRegistration.Read(exe, store, p => true).Available && store.Writes == 0, "registry read failure is visible and disables uncertain toggle");
            store = new Store { FailAfterWrite = true }; failure = null;
            try { StartupRegistration.Set(true, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(store.Value == null && failure != null && failure.Contains("已确认保留"), "partial startup write is restored and verified");
            store = new Store { Value = command, FailAfterWrite = true }; failure = null;
            try { StartupRegistration.Set(false, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(store.Value == command && failure != null && failure.Contains("已确认保留"), "failed startup deletion restores previous command");
            store = new Store { IgnoreWrites = true }; failure = null;
            try { StartupRegistration.Set(true, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && store.Value == null, "silently ignored startup write is detected");
            store = new Store { FailAfterWrite = true, FailRecovery = true }; failure = null;
            try { StartupRegistration.Set(true, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && failure.Contains("未能确认恢复") && !failure.Contains("已确认保留"), "startup recovery failure is not reported as success");
            failure = null;
            try { StartupRegistration.Command(@"C:\" + new string('a', 250) + @"\AudioSwitch.exe"); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null, "startup command respects Windows Run length limit");
            failure = null;
            try { StartupRegistration.Command(@"C:\renamed\AudioSwitch-Copy.exe"); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null, "renamed executable is rejected before creating an unrecognizable startup entry");
            store = new Store { FailedReads = new HashSet<int> { 2, 3 } }; failure = null;
            try { StartupRegistration.Set(true, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && failure.Contains("已确认保留") && store.Value == null && store.Writes == 2, "read failures after startup write still attempt restoration and verify it");
            store = new Store { Value = command }; failure = null;
            try { StartupRegistration.Set(false, exe, store, p => true, true, null); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && store.Writes == 0 && store.Value == command, "stale UI snapshot cannot overwrite a changed startup registration");
            string other = StartupRegistration.Command(@"C:\different\AudioSwitch.exe");
            store = new Store { AfterWrite = s => { s.Value = other; } }; failure = null;
            try { StartupRegistration.Set(true, exe, store, p => true); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && store.Value == other && store.Writes == 1, "rollback never overwrites an observed external startup change");
            RunLocationTests(check);
        }
        private static void RunLocationTests(Action<bool, string> check)
        {
            string work = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-tests", Guid.NewGuid().ToString("N"));
            string oldDirectory = Path.Combine(work, "旧位置"), newDirectory = Path.Combine(work, "新位置 空格 & ()");
            Directory.CreateDirectory(oldDirectory); Directory.CreateDirectory(newDirectory);
            string oldExe = Path.Combine(oldDirectory, "AudioSwitch.exe"), newExe = Path.Combine(newDirectory, "AudioSwitch.exe");
            File.WriteAllText(oldExe, "location fixture only; never executed");
            var store = new Store(); StartupRegistration.Set(true, oldExe, store);
            string oldCommand = store.Value;
            File.Move(oldExe, newExe);
            var state = StartupRegistration.Read(newExe, store);
            check(state.Available && !state.Enabled && state.NeedsRepair && state.Message.Contains("失效") && state.Details.Contains(oldExe) && state.Details.Contains(newExe) && store.Writes == 1, "moved executable reports broken old path and both locations without rewriting startup");
            string failure = null;
            try { StartupRegistration.Set(true, oldExe, store); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && store.Value == oldCommand && store.Writes == 1 && !StartupRegistration.Read(oldExe, store).Available, "still-running old location cannot register missing executable");
            StartupRegistration.Set(true, newExe, store, null, true, state.RegisteredCommand);
            check(StartupRegistration.Read(newExe, store).Enabled && store.Value == StartupRegistration.Command(newExe), "explicit repair switches registration to real relocated executable");
            File.Copy(newExe, oldExe);
            state = StartupRegistration.Read(oldExe, store);
            check(state.NeedsRepair && state.Message.Contains("其他位置") && !state.Message.Contains("失效"), "existing alternate installation is distinguished from broken old path");
            failure = null;
            try { InstanceLocation.RequireSame(newExe, oldExe); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null && failure.Contains(oldExe) && failure.Contains(newExe), "different backend location blocks forwarding with both paths and exit instructions");
            InstanceLocation.RequireSame(newExe, newExe.ToUpperInvariant());
            check(true, "backend path check accepts case-insensitive same path");
            failure = null;
            try { InstanceLocation.RequireSame(newExe, null); } catch (Exception ex) { failure = ex.Message; }
            check(failure != null, "unknown startup request origin is rejected");
        }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control control in parent.Controls) { yield return control; foreach (var child in Descendants(control)) yield return child; }
        }
        internal static void Render(Action<bool, string> check)
        {
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(output);
            foreach (bool dark in new[] { false, true })
            {
                var requests = new List<Request>(); bool fail = false;
                var reply = new Reply { State = new AudioState(), Pending = new List<Arrival>(), Preferences = new Preferences { DarkMode = dark }, Startup = new StartupState { Available = true } };
                using (var form = new Dashboard(false, false, request => {
                    requests.Add(request);
                    if (request.Action == "startup") { if (!fail) reply.Startup.Enabled = request.Value; reply.Error = fail ? "自启保存失败" : null; }
                    return Task.FromResult(reply);
                }))
                {
                    form.Show(); Application.DoEvents();
                    var toggle = Descendants(form).OfType<CheckBox>().Single(c => c.Name == "startWithWindows");
                    check(toggle.Enabled && !toggle.Checked, "startup UI reads off state without writing");
                    toggle.Checked = true;
                    check(toggle.Checked && reply.Startup.Enabled && requests.Last().Action == "startup" && requests.Last().StartupExecutablePath == Application.ExecutablePath, "startup UI binds its request to this executable location");
                    fail = true; toggle.Checked = false;
                    check(toggle.Checked && reply.Startup.Enabled, "failed startup change restores confirmed checkbox state");
                    fail = false; reply.Error = null; form.RenderReply(reply);
                    form.Size = form.MinimumSize; Application.DoEvents();
                    check(toggle.Parent.ClientRectangle.Contains(toggle.Bounds) && toggle.Parent.Controls.Cast<Control>().Where(c => c != toggle).All(c => !c.Bounds.IntersectsWith(toggle.Bounds)), "startup toggle fits minimum dashboard without overlapping controls");
                    using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, dark ? "startup-dark.png" : "startup-light.png")); }
                    reply.Startup.Available = false; reply.Startup.Message = "无法读取自启设置"; form.RenderReply(reply);
                    check(!toggle.Enabled && toggle.Checked, "read failure disables startup editing while retaining confirmed supplied state");
                    reply.Startup = new StartupState { Available = true, CurrentExecutablePath = @"C:\old\AudioSwitch.exe", Enabled = true };
                    form.RenderReply(reply);
                    check(!toggle.Enabled && !toggle.Checked && Descendants(form).Single(c => c.Name == "startupDetails").Text.Contains("另一位置"), "frontend connected to another backend disables misleading startup toggle");
                    reply.Startup = new StartupState { Available = true, NeedsRepair = true, Message = "原自启路径已失效，点击查看", Details = "原登记路径：C:\\old\\AudioSwitch.exe\r\n当前程序路径：" + Application.ExecutablePath,
                        RegisteredCommand = "\"C:\\old\\AudioSwitch.exe\" --background" };
                    form.RenderReply(reply);
                    using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, dark ? "startup-moved-dark.png" : "startup-moved-light.png")); }
                    bool detailsShown = false;
                    using (var closeDetails = new System.Windows.Forms.Timer { Interval = 40 })
                    {
                        closeDetails.Tick += delegate {
                            var dialog = Application.OpenForms.OfType<NoticeDialog>().FirstOrDefault();
                            if (dialog == null) return;
                            closeDetails.Stop();
                            detailsShown = Descendants(dialog).OfType<Label>().Any(l => l.Text.Contains(@"C:\old\AudioSwitch.exe") && l.Text.Contains(Application.ExecutablePath));
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, dark ? "startup-path-details-dark.png" : "startup-path-details-light.png")); }
                            dialog.Close();
                        };
                        closeDetails.Start();
                        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(Descendants(form).Single(c => c.Name == "startupDetails"), new object[] { EventArgs.Empty });
                    }
                    check(detailsShown, "clicking relocated startup hint shows both old and current paths");
                    toggle.Checked = true;
                    check(requests.Last().ExpectedStartupCommand == reply.Startup.RegisteredCommand, "relocation repair carries the exact observed old startup command");
                    check(requests.All(r => r.Action == "snapshot" || r.Action == "startup"), "startup UI makes no audio or preference write requests");
                    form.Close();
                }
            }
            Palette.Apply(false);
        }
        internal static void BackgroundProcess(Action<bool, string> check)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe");
            foreach (string name in new[] { "Local\\AudioSwitch-Host-" + Wire.Identity, UpdateInstaller.MutexName })
            {
                bool created;
                using (var mutex = new Mutex(true, name, out created))
                {
                    if (!created) throw new InvalidOperationException("请先退出声间及更新助手，再运行后台启动隔离测试。");
                    try
                    {
                        using (var process = Process.Start(new ProcessStartInfo(executable, "--background") { UseShellExecute = false, CreateNoWindow = true }))
                        {
                            bool exited = process.WaitForExit(4000);
                            if (!exited && UpdateInstaller.Matches(process, executable)) { process.Kill(); process.WaitForExit(2000); }
                            check(exited && process.ExitCode == 0, name == UpdateInstaller.MutexName ? "background startup exits silently during update without audio initialization" : "duplicate background startup exits silently without contacting frontend or initializing audio");
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
        }
    }
}
