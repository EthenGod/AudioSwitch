// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace AudioSwitch
{
    internal static class CandidateUpdateTests
    {
        internal static void Run(Action<bool, string> check)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "candidate-update-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string legacy = Path.Combine(root, "legacy"); CopyLegacy(legacy);
            check(!PanelLaunch.IsCandidate(legacy) && FileIntegrity.Check(legacy).Passed, "legacy distribution retains required-file checks");
            foreach (string marker in PanelLaunch.Files.Skip(8).Concat(new[] { "candidate-manifest.json" }))
            {
                string directory = Path.Combine(root, marker); CopyLegacy(directory);
                File.WriteAllText(Path.Combine(directory, marker), "damaged candidate marker");
                var result = FileIntegrity.Check(directory);
                check(result.Candidate && !result.Passed && result.Message.Contains("完整候选包") && !result.Message.Contains("不要求补齐"), "surviving candidate marker requires full check: " + marker);
                Reject(() => PanelLaunch.Select(Path.Combine(directory, "AudioSwitch.exe"), false, 123), check, "damaged candidate never silently launches old UI: " + marker);
                string before = Snapshot(directory), backup = Path.Combine(root, "backup-" + marker);
                int writes = 0;
                Reject(() => UpdateInstaller.ApplyFiles(legacy, directory, backup, name => writes++), check, "legacy transaction rejects candidate target: " + marker);
                Reject(() => UpdateInstaller.ApplySelectedFiles(legacy, directory, backup, FileIntegrity.RequiredFiles, name => writes++), check, "legacy repair transaction rejects candidate target: " + marker);
                var repair = FileRepair.Run(directory, CancellationToken.None, text => writes++, token => { writes++; return legacy; }, name => writes++);
                check(!repair.Check.Passed && repair.Payload == null && repair.Notes.Contains("局部修复") && writes == 0 && !Directory.Exists(backup) && before == Snapshot(directory), "candidate repair never downloads, backs up or replaces files: " + marker);
                Reject(() => AppUpdate.ValidatePayload(directory, AppUpdate.ParseVersion(AppVersion.Number)), check, "legacy payload validation rejects extra candidate files: " + marker);
                string oldBefore = Snapshot(legacy);
                Reject(() => AppUpdate.Extract(Path.Combine(root, "unused.zip"), directory, CancellationToken.None, true), check, "legacy extraction cannot overwrite candidate payload cache: " + marker);
                Reject(() => UpdateInstaller.ApplyFiles(directory, legacy, backup, name => writes++), check, "candidate payload cannot be partially applied to legacy target: " + marker);
                check(oldBefore == Snapshot(legacy) && !Directory.Exists(backup) && writes == 0, "legacy target remains untouched after candidate payload rejection: " + marker);
            }
            string blocked = Path.Combine(root, "blocked"); CopyLegacy(blocked);
            Directory.CreateDirectory(Path.Combine(blocked, "audio-switch-panel.exe"));
            check(PanelLaunch.IsCandidate(blocked) && !FileIntegrity.Check(blocked).Passed, "directory masquerading as panel still blocks legacy fallback");
            string data = Path.Combine(root, "isolated-data");
            var store = new AutomaticUpdateStore(data, Path.Combine(blocked, "AudioSwitch.exe"));
            var release = new UpdateRelease { Version = new Version(99, 0, 0), Tag = "v99.0.0" };
            Reject(() => store.Check(CancellationToken.None), check, "candidate automatic check stops before network");
            Reject(() => store.Prepare(release, CancellationToken.None), check, "candidate automatic download stops before cache creation");
            Reject(() => store.Stage(release, legacy), check, "candidate cannot stage old update record");
            bool launched = false; string issue;
            check(!store.TryInstall(false, (path, item, background) => launched = true, out issue) && issue.Contains("旧格式") && !launched && !Directory.Exists(data), "candidate startup blocks installation without creating update cache");
            Directory.CreateDirectory(Path.GetDirectoryName(store.PathName));
            File.WriteAllText(store.PathName, "original pending record, intentionally invalid");
            string pendingBefore = Snapshot(data);
            string download = Path.Combine(root, "download");
            Directory.CreateDirectory(Path.Combine(download, "payload"));
            File.WriteAllText(Path.Combine(download, "payload", "candidate-manifest.json"), "preserve cached candidate");
            string downloadBefore = Snapshot(download);
            Reject(() => AppUpdate.PrepareInDirectory(release, CancellationToken.None, null, download), check, "legacy download rejects candidate cache before opening network or lock");
            check(downloadBefore == Snapshot(download), "rejected download preserves cached candidate");
            check(!store.TryInstall(false, (path, item, background) => launched = true, out issue) && !launched && pendingBefore == Snapshot(data), "candidate leaves preexisting pending record unchanged even if invalid");
            using (var update = new BackgroundUpdate(store, null))
            {
                update.Start(); bool done = update.Work.Wait(3000);
                check(done && update.Finished && !update.NeedsActivity && update.Snapshot().Stage == "error" && update.Snapshot().Message.Contains("整包升级"), "candidate background check finishes without polling, download or retry");
            }
            check(pendingBefore == Snapshot(data), "background rejection preserves all pending cache contents");
            var failed = PanelMaintenance.Run("files", CancellationToken.None, null, () => FileIntegrity.Check(blocked));
            check(failed.Status == "failed" && failed.Message.Contains("完整候选包") && failed.Message.Contains("没有修改"), "panel reports candidate failure with actionable readonly guidance");
            var passed = PanelMaintenance.Run("files", CancellationToken.None, null, () => new IntegrityResult { Candidate = true });
            check(passed.Status == "passed" && passed.Message == "候选整包文件检查通过。", "panel distinguishes whole candidate check from legacy runtime check");
        }
        private static void CopyLegacy(string directory)
        {
            foreach (string name in AppUpdate.Files) {
                string destination = Path.Combine(directory, name); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), destination);
            }
        }
        private static string Snapshot(string directory)
        {
            return String.Join("\n", Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => path.Substring(directory.Length) + (File.Exists(path) ? "=" + AppUpdate.Hash(path) : "/")));
        }
        private static void Reject(Action action, Action<bool, string> check, string name)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException ex) { rejected = ex.Message.Contains("候选"); } catch (InvalidOperationException ex) { rejected = ex.Message.Contains("候选"); }
            check(rejected, name);
        }
    }
}
