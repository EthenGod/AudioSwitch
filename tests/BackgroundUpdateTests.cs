// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace AudioSwitch
{
    internal static class BackgroundUpdateTests
    {
        private static void Wait(BackgroundUpdate updater)
        { if (!updater.Work.Wait(5000)) throw new Exception("Background update task timed out."); }
        internal static void Run(Action<bool, string> check)
        {
            var newer = new UpdateRelease { Version = new Version(99, 0, 0), Tag = "v99.0.0" };
            int checks = 0, downloads = 0, staged = 0;
            using (var updater = new BackgroundUpdate(token => { checks++; return newer; },
                (release, token) => { downloads++; return "verified-payload"; }, (release, path) => staged++, () => DateTime.UtcNow))
            {
                updater.Start(); updater.Start(); Wait(updater);
                check(checks == 1 && updater.Snapshot().Stage == "waiting" && downloads == 0, "startup checks once without downloading or installing immediately");
                updater.Tick(false, false); updater.Tick(true, true);
                check(downloads == 0, "active input and pending audio work both defer download");
                updater.Tick(true, false); Wait(updater);
                updater.Tick(true, false);
                check(downloads == 1 && staged == 1 && updater.Finished && updater.Snapshot().Stage == "ready", "idle download is staged once and does not restart this session");
                var reply = Wire.Decode<Reply>(Wire.Encode(new Reply { Update = updater.Snapshot(), Pending = new System.Collections.Generic.List<Arrival>() }));
                check(reply.Update.Version == newer.Tag && reply.Error == null && reply.Pending.Count == 0, "update status crosses IPC separately from audio errors and arrivals");
            }
            var clock = DateTime.UtcNow; downloads = 0;
            using (var updater = new BackgroundUpdate(token => newer, (release, token) => { downloads++; throw new IOException("offline"); },
                (release, path) => { throw new Exception("must not stage failed downloads"); }, () => clock))
            {
                updater.Start(); Wait(updater); updater.Tick(true, false); Wait(updater);
                updater.Tick(true, false);
                check(downloads == 1 && updater.Snapshot().Stage == "waiting" && !updater.NeedsActivity, "failed download waits five minutes without performance sampling before retry");
                clock = clock.AddMinutes(5); updater.Tick(true, false); Wait(updater);
                clock = clock.AddMinutes(5); updater.Tick(true, false); Wait(updater);
                clock = clock.AddHours(1); updater.Tick(true, false);
                check(downloads == 3 && updater.Finished && updater.Snapshot().Stage == "error", "automatic download stops after three failures per session");
            }
            using (var entered = new ManualResetEvent(false))
            using (var updater = new BackgroundUpdate(token => newer, (release, token) => {
                entered.Set(); token.WaitHandle.WaitOne(5000); token.ThrowIfCancellationRequested(); return "unused";
            }, (release, path) => staged++, () => clock))
            {
                updater.Start(); Wait(updater); updater.Tick(true, false);
                check(entered.WaitOne(5000), "download worker starts independently of UI thread");
                updater.Dispose(); Wait(updater);
                check(staged == 1, "exiting during download cancels without scheduling an installation");
            }
            foreach (var blocked in new[] { new Version(99, 0, 0), new Version(98, 0, 0) })
            using (var updater = new BackgroundUpdate(token => newer, (release, token) => "unused", (release, path) => { }, () => clock, blocked))
            {
                updater.Start(); Wait(updater);
                check(updater.Snapshot().Stage == (blocked == newer.Version ? "error" : "waiting"), "failed automatic version is blocked but a newer version remains eligible: " + blocked);
            }
            using (var updater = new BackgroundUpdate(token => new UpdateRelease { Version = new Version(0, 0, 1) },
                (release, token) => "unused", (release, path) => { }, () => clock))
            { updater.Start(); Wait(updater); check(updater.Finished && updater.Snapshot().Message == null, "no newer release means no UI reminder or idle timer work"); }
            using (var updater = new BackgroundUpdate(token => { throw new IOException("offline"); },
                (release, token) => "unused", (release, path) => { }, () => clock))
            { updater.Start(); Wait(updater); check(updater.Finished && updater.Snapshot().Stage == "error", "startup network failure is captured as panel status"); }
            check(UpdateInstaller.RestartArguments(new UpdatePlan { Automatic = true, Background = true }, "event") == "--background --updated=event", "automatic login update restarts only the tray");
            check(UpdateInstaller.RestartArguments(new UpdatePlan { Automatic = true }, "event") == "--updated=event", "explicit app launch retains the requested panel after update");
            Store(check, newer);
            ActivityAndStorage(check, newer);
        }
        internal static void NativeActivity(Action<bool, string> check)
        {
            using (var activity = new UpdateActivity())
            {
                var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                var watch = Stopwatch.StartNew(); activity.ReadBusy(false); watch.Stop();
                Thread.Sleep(1100); watch.Start(); activity.ReadBusy(false); watch.Stop();
                Thread.Sleep(1100); watch.Start(); activity.ReadBusy(false); watch.Stop();
                Console.WriteLine(activity.Diagnostic);
                check(activity.Diagnostic != null && activity.Diagnostic.Contains("; CPU="), "live Windows CPU/GPU/disk/memory and fullscreen queries return usable samples");
                Console.WriteLine("Three reads including setup: wall=" + watch.ElapsedMilliseconds + " ms; CPU=" + (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds.ToString("F1") + " ms; fullscreen=" + UpdateActivity.Fullscreen());
            }
        }
        private static void ActivityAndStorage(Action<bool, string> check, UpdateRelease release)
        {
            check(!UpdateActivity.Busy(10, 20, 10, 10, 30, false), "low load allows automatic download eligibility");
            check(UpdateActivity.Busy(50, 20, 10, 10, 30, false) && UpdateActivity.Busy(10, 85, 10, 10, 30, false), "total CPU and a saturated core each defer download");
            check(UpdateActivity.Busy(10, 20, 60, 10, 30, false) && UpdateActivity.Busy(10, 20, 10, 80, 30, false) && UpdateActivity.Busy(10, 20, 10, 10, 80, false), "GPU, disk and memory pressure each defer download");
            check(UpdateActivity.Busy(10, 20, 10, 10, 30, true) && UpdateActivity.Busy(Double.NaN, 20, 10, 10, 30, false), "fullscreen or an unreadable load sample cannot count as idle");
            check(UpdateActivity.Covers(-1920, 0, 0, 1080, -1920, 0, 0, 1080) && !UpdateActivity.Covers(0, 0, 1920, 1040, 0, 0, 1920, 1080), "secondary-monitor fullscreen recognized while ordinary maximized work area stays eligible");
            check(UpdateActivity.EngineKey("pid_10_luid_1_phys_0_eng_2") == UpdateActivity.EngineKey("pid_20_luid_1_phys_0_eng_2") && UpdateActivity.EngineKey("pid_10_luid_1_phys_0_eng_2") != UpdateActivity.EngineKey("pid_10_luid_1_phys_0_eng_3"), "GPU aggregation groups processes on the same engine without adding different engines");
            int downloads = 0, staged = 0; var clock = DateTime.UtcNow;
            using (var entered = new ManualResetEvent(false))
            using (var updater = new BackgroundUpdate(token => release, (found, token) => {
                downloads++; if (downloads == 1) { entered.Set(); token.WaitHandle.WaitOne(5000); token.ThrowIfCancellationRequested(); } return "payload";
            }, (found, path) => staged++, () => clock))
            {
                updater.Start(); Wait(updater); updater.Tick(true, false); check(entered.WaitOne(5000), "download starts before injected fullscreen/load event");
                updater.Tick(true, true); Wait(updater);
                check(staged == 0 && updater.Snapshot().Stage == "waiting", "busy event cancels in-flight download without scheduling installation");
                clock = clock.AddMinutes(1); updater.Tick(true, true);
                check(downloads == 1, "continued high load prevents automatic retry");
                updater.Tick(true, false); Wait(updater);
                check(downloads == 2 && staged == 1, "download retries after pressure clears");
            }
            using (var updater = new BackgroundUpdate(token => release, (found, token) => { throw new UpdateStorageException("cache full"); }, (found, path) => staged++, () => clock))
            {
                updater.Start(); Wait(updater); updater.Tick(true, false); Wait(updater);
                check(updater.Finished && updater.Snapshot().Stage == "error" && updater.Snapshot().Message.Contains("cache full"), "storage cap stops automatic retries immediately with a panel explanation");
            }
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "storage-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string keep = Path.Combine(root, "keep.txt"); File.WriteAllText(keep, "12345678");
            UpdateStorage.Require(root, 16, 8, "test"); bool rejected = false;
            try { UpdateStorage.Require(root, 16, 9, "test"); } catch (UpdateStorageException) { rejected = true; }
            check(rejected && File.ReadAllText(keep) == "12345678", "cache reservation rejects growth beyond cap without deleting existing files");
            using (UpdateStorage.CacheLease(root))
            {
                rejected = false; try { using (UpdateStorage.CacheLease(root)) { } } catch (IOException) { rejected = true; }
                check(rejected, "cache writers cannot reserve space concurrently");
            }
        }
        private static void Store(Action<bool, string> check, UpdateRelease release)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "automatic-tests", Guid.NewGuid().ToString("N"));
            string work = Path.Combine(root, "updates", Guid.NewGuid().ToString("N")), payload = Path.Combine(work, "payload");
            foreach (string name in AppUpdate.Files)
            {
                string file = Path.Combine(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), file);
            }
            string source = Path.Combine(work, "fixture.cs");
            File.WriteAllText(source, "[assembly:System.Reflection.AssemblyVersion(\"99.0.0\")] class Fixture { static void Main() {} }");
            string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            using (var compile = Process.Start(new ProcessStartInfo(compiler, "/nologo /target:winexe /platform:x64 /out:" + UpdateInstaller.Quote(Path.Combine(payload, "AudioSwitch.exe")) + " " + UpdateInstaller.Quote(source)) { UseShellExecute = false, CreateNoWindow = true }))
                if (!compile.WaitForExit(15000) || compile.ExitCode != 0) throw new Exception("Cannot compile automatic update fixture.");
            string zip = Path.Combine(work, "package.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (string name in AppUpdate.Files) archive.CreateEntryFromFile(Path.Combine(payload, name), name);
            release.Digest = AppUpdate.Hash(zip);
            release.Size = new FileInfo(zip).Length;
            int beforeFiles = Directory.GetFiles(work, "*", SearchOption.AllDirectories).Length;
            string retried = AppUpdate.PrepareInDirectory(release, CancellationToken.None, null, work);
            check(retried == payload && Directory.GetFiles(work, "*", SearchOption.AllDirectories).Length == beforeFiles && AppUpdate.Hash(zip) == release.Digest, "verified ZIP and fixed payload files are reused without a network request or duplicate attempt directory");
            string target = Path.Combine(root, "installed", "AudioSwitch.exe");
            var store = new AutomaticUpdateStore(root, target);
            check(store.Read() == null, "no staged update leaves ordinary startup unchanged");
            store.Stage(release, payload); string original = File.ReadAllText(store.PathName);
            check(!store.Read().Attempted && store.Read().Version == "99.0.0", "verified download persists across application sessions");
            check(new AutomaticUpdateStore(root, Path.Combine(root, "moved", "AudioSwitch.exe")).Read() == null, "moving executable cannot install cache bound to its old path");
            int launches = 0; string issue;
            check(store.TryInstall(true, (path, found, background) => { check(background && found.Tag == release.Tag && path == payload, "startup handoff retains exact payload and launch mode"); launches++; }, out issue) && issue == null, "next startup accepts a fully verified cached package");
            check(!store.TryInstall(true, (path, found, bg) => launches++, out issue) && launches == 1 && issue != null, "attempt marker prevents repeated installation after interrupted handoff");
            File.WriteAllText(store.PathName, original);
            check(!store.TryInstall(false, (path, found, bg) => { throw new IOException("helper failed"); }, out issue) && store.Read().Attempted && issue.Contains("helper failed"), "helper startup failure preserves a visible retry reason and blocks auto loops");
            File.WriteAllText(store.PathName, original);
            string notice = Path.Combine(payload, "NOTICE.txt"), originalNotice = File.ReadAllText(notice);
            File.WriteAllText(notice, "changed after download");
            check(!store.TryInstall(true, (path, found, bg) => launches++, out issue) && launches == 1 && issue != null, "changed extracted file blocks cached update before any helper starts");
            File.WriteAllText(notice, originalNotice); File.WriteAllText(store.PathName, original);
            using (var stream = new FileStream(zip, FileMode.Append)) stream.WriteByte(0);
            check(!store.TryInstall(true, (path, found, bg) => launches++, out issue) && launches == 1, "changed ZIP digest blocks next-start installation");
            File.WriteAllText(store.PathName, original); var current = store.Read();
            current.Version = AppVersion.Number; current.Tag = "v" + AppVersion.Number; store.Save(current);
            check(!store.TryInstall(true, (path, found, bg) => launches++, out issue) && issue == null, "already installed version ignores obsolete cache without downgrade");
            File.WriteAllText(store.PathName, original); var traversal = store.Read(); traversal.WorkId = "../outside"; store.Save(traversal);
            check(!store.TryInstall(true, (path, found, bg) => launches++, out issue) && launches == 1, "untrusted cache path cannot escape update workspace");
        }
    }
}
