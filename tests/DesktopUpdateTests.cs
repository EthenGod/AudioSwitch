// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace AudioSwitch
{
    internal static class DesktopUpdateTests
    {
        private static bool Reject(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        private static Dictionary<string, object> Asset(string version, int format)
        {
            string name = UpdatePackage.AssetName(AppUpdate.ParseVersion(version), format);
            return new Dictionary<string, object> { { "name", name }, { "browser_download_url", AppUpdate.ReleasesUrl + "/download/v" + version + "/" + name }, { "size", 4096 }, { "digest", "sha256:" + new string('a', 64) } };
        }
        private static string Release(string version, params object[] assets)
        {
            return Wire.Encode(new Dictionary<string, object> { { "tag_name", "v" + version }, { "draft", false }, { "prerelease", false }, { "assets", assets } });
        }
        private static string Row(Dictionary<string, object> asset)
        { return "<li><a href='" + asset["browser_download_url"] + "'>package</a><clipboard-copy value='" + asset["digest"] + "'></clipboard-copy></li>"; }
        internal static void MakeDesktop(string directory, string version)
        {
            foreach (string name in UpdatePackage.DesktopContent.Skip(8)) File.WriteAllText(Path.Combine(directory, name), "fixture " + name);
            File.Copy(Path.Combine(directory, "AudioSwitch.exe"), Path.Combine(directory, "audio-switch-panel.exe"), true);
            Manifest(directory, version);
        }
        internal static void Manifest(string directory, string version)
        {
            var rows = UpdatePackage.DesktopContent.Select(name => new Dictionary<string, object> {
                { "Path", name }, { "Length", new FileInfo(Path.Combine(directory, name)).Length }, { "Sha256", AppUpdate.Hash(Path.Combine(directory, name)) }
            }).ToArray();
            File.WriteAllText(Path.Combine(directory, "desktop-manifest.json"), Wire.Encode(new Dictionary<string, object> {
                { "Format", "AudioSwitch.Desktop" }, { "FormatVersion", 1 }, { "AppVersion", version }, { "Platform", "win-x64" }, { "Files", rows }
            }));
        }
        private static void Legacy(string directory)
        {
            foreach (string name in AppUpdate.Files) {
                string file = Path.Combine(directory, name); Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), file);
            }
            File.WriteAllText(Path.Combine(directory, "settings.json"), "leave user data untouched");
        }
        private static string Snapshot(string directory)
        {
            return String.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
                .Select(path => path.Substring(directory.Length) + "=" + AppUpdate.Hash(path)));
        }
        private static void Zip(string directory, string zip)
        { using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) foreach (string name in UpdatePackage.DesktopFiles) archive.CreateEntryFromFile(Path.Combine(directory, name), name); }
        internal static void Run(Action<bool, string> check)
        {
            var current = AppUpdate.ParseVersion(AppVersion.Number);
            var old = Asset("99.0.0", UpdatePackage.Legacy); var modern = Asset("99.0.0", UpdatePackage.Desktop);
            var selected = AppUpdate.ParseRelease(Release("99.0.0", old, modern));
            check(selected.PackageFormat == UpdatePackage.Desktop && selected.Url.Contains("Desktop-v99.0.0"), "bridge prefers full desktop asset while legacy asset stays available");
            check(AppUpdate.ParseRelease(Release("99.0.0", old)).PackageFormat == UpdatePackage.Legacy, "legacy-only release remains supported");
            check(Reject(() => AppUpdate.ParseRelease(Release("99.0.0", old, modern, modern))), "duplicate desktop assets cannot fall back to legacy");
            foreach (string field in new[] { "digest", "size", "browser_download_url" }) {
                var bad = Asset("99.0.0", UpdatePackage.Desktop); bad[field] = "bad";
                check(Reject(() => AppUpdate.ParseRelease(Release("99.0.0", old, bad))), "bad desktop metadata blocks fallback: " + field);
            }
            string same = Release(AppVersion.Number, Asset(AppVersion.Number, 0), Asset(AppVersion.Number, 1));
            check(AppUpdate.ParseRelease(same, true).PackageFormat == 1 && AppUpdate.ParseRelease(same).Url == null, "only migration discovery resolves same-version desktop assets");
            var html = new UpdateRelease { Version = new Version(99, 0, 0), Tag = "v99.0.0" };
            AppUpdate.ParseAssetPage(html, Row(old) + Row(modern));
            check(html.PackageFormat == 1 && html.Digest.Length == 64, "HTML fallback selects desktop with exact digest");
            modern["digest"] = "invalid";
            check(Reject(() => AppUpdate.ParseAssetPage(html, Row(old) + Row(modern))), "HTML bad desktop digest cannot fall back to legacy");
            check(Reject(() => AppUpdate.Check(CancellationToken.None, (url, token) => new UpdatePage {
                Address = new Uri(url.EndsWith("/latest") && url != AppUpdate.ApiUrl ? AppUpdate.ReleasesUrl + "/tag/v99.0.0" : url),
                Text = url == AppUpdate.ApiUrl ? Release("99.0.0", old, modern) : url.Contains("expanded_assets") ? Row(old) : ""
            })), "API observed desktop cannot fall back to an HTML legacy asset");

            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dt-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string payload = Path.Combine(root, "payload"); Legacy(payload); MakeDesktop(payload, AppVersion.Number);
            AppUpdate.ValidatePayload(payload, current, 1);
            check(FileIntegrity.Check(payload).Passed && FileIntegrity.Check(payload).Desktop && !PanelLaunch.IsCandidate(payload), "actual backend with desktop fixture manifest passes complete integrity check");
            check(PanelLaunch.Select(Path.Combine(payload, "AudioSwitch.exe"), false, 123).FileName.EndsWith("audio-switch-panel.exe"), "desktop layout launches panel through validated path");
            check(Reject(() => AppUpdate.ValidatePayload(payload, current)), "desktop payload cannot use legacy validator");
            string target = Path.Combine(root, "installed"); Legacy(target);
            check(UpdatePackage.CanInstall(current, 1, target) && !UpdatePackage.CanInstall(current, 0, target), "same-version migration only accepts desktop format");
            check(!UpdatePackage.CanInstall(current, 1, payload) && Reject(() => UpdateInstaller.ValidateVersion(current, false, 1, payload)), "same-version desktop reinstall is not treated as migration");
            check(Reject(() => UpdateInstaller.ValidateVersion(new Version(0, 0, 1), false, 1, target)), "desktop migration never permits a version downgrade");
            check(Reject(() => UpdatePackage.RequireTarget(payload, 0)) && Reject(() => UpdatePackage.Files(7)), "desktop downgrade and unknown package format rejected");
            var maintenance = PanelMaintenance.Run("files", CancellationToken.None, null, () => FileIntegrity.Check(payload));
            check(maintenance.Status == "passed" && maintenance.Message.Contains("桌面整包"), "maintenance reports desktop complete check");
            string zip = Path.Combine(root, "desktop.zip"); Zip(payload, zip);
            string extracted = Path.Combine(root, "中文 & (新包)"); AppUpdate.Extract(zip, extracted, CancellationToken.None, false, 1);
            AppUpdate.ValidatePayload(extracted, current, 1);
            check(UpdatePackage.DesktopFiles.All(name => AppUpdate.Hash(Path.Combine(extracted, name)) == AppUpdate.Hash(Path.Combine(payload, name))), "desktop ZIP extracts exact complete files under Chinese path");
            check(Reject(() => AppUpdate.Extract(zip, Path.Combine(root, "wrong-format"), CancellationToken.None)) && !Directory.Exists(Path.Combine(root, "wrong-format")), "old extractor rejects 14-file ZIP before writing");
            foreach (string mode in new[] { "extra", "duplicate", "traversal", "symlink", "reparse", "oversize", "missing" }) {
                string badZip = Path.Combine(root, mode + ".zip");
                using (var archive = ZipFile.Open(badZip, ZipArchiveMode.Create)) {
                    foreach (string name in UpdatePackage.DesktopFiles) {
                        if (mode == "missing" && name == "desktop-manifest.json") continue;
                        var entry = mode == "oversize" && name == "desktop-manifest.json" ? archive.CreateEntry(name) : archive.CreateEntryFromFile(Path.Combine(payload, name), name);
                        if (mode == "oversize" && name == "desktop-manifest.json") using (var writer = new StreamWriter(entry.Open())) writer.Write(new string('x', 1024 * 1024 + 1));
                        if (mode == "symlink" && name == "desktop-manifest.json") entry.ExternalAttributes = unchecked((int)0xa0000000);
                        if (mode == "reparse" && name == "desktop-manifest.json") entry.ExternalAttributes = 0x400;
                    }
                    if (mode == "extra" || mode == "duplicate" || mode == "traversal") {
                        using (var writer = new StreamWriter(archive.CreateEntry(mode == "duplicate" ? "LICENSE" : mode == "traversal" ? "../escape" : "settings.json").Open())) writer.Write("bad");
                    }
                }
                string output = Path.Combine(root, "out-" + mode);
                check(Reject(() => AppUpdate.Extract(badZip, output, CancellationToken.None, false, 1)) && !Directory.Exists(output), "desktop malformed ZIP rejected before any writes: " + mode);
            }
            string before = Snapshot(target);
            for (int i = 0; i < UpdatePackage.DesktopFiles.Length; i++) {
                string failing = UpdatePackage.DesktopFiles[i], backup = Path.Combine(root, "rollback-" + i); bool restored = false;
                try { UpdateInstaller.ApplyPackage(payload, target, backup, current, 1, name => { if (name == failing) throw new IOException("injected write failure"); }); }
                catch (UpdateTransactionException ex) { restored = ex.Restored; }
                check(restored && before == Snapshot(target) && !PanelLaunch.IsModern(target), "migration rollback restores every attempted file and removes new markers: " + failing);
            }
            using (var locked = new FileStream(Path.Combine(target, "AudioSwitch.exe"), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                bool restored = false;
                try { UpdateInstaller.ApplyPackage(payload, target, Path.Combine(root, "locked"), current, 1); } catch (UpdateTransactionException ex) { restored = ex.Restored; }
                check(restored && before == Snapshot(target), "locked target never leaves a partial migration");
            }
            UpdateInstaller.ApplyPackage(payload, target, Path.Combine(root, "success"), current, 1);
            check(FileIntegrity.Check(target).Passed && File.ReadAllText(Path.Combine(target, "settings.json")) == "leave user data untouched", "migration installs complete desktop package and preserves unrelated settings");
            string desktopBefore = Snapshot(target);
            bool fullRestored = false;
            try { UpdateInstaller.ApplyPackage(payload, target, Path.Combine(root, "desktop-rollback"), current, 1, name => { if (name == "desktop-manifest.json") throw new IOException("failed at final file"); }); }
            catch (UpdateTransactionException ex) { fullRestored = ex.Restored; }
            check(fullRestored && desktopBefore == Snapshot(target), "desktop-to-desktop rollback restores all 14 existing files");
            string brokenTarget = Path.Combine(root, "recovery-failure"); Legacy(brokenTarget);
            string brokenBackup = Path.Combine(root, "bad-backup"); bool failedRecovery = false;
            try { UpdateInstaller.ApplyPackage(payload, brokenTarget, brokenBackup, current, 1, name => {
                if (name == "AudioSwitch.exe") { File.WriteAllText(Path.Combine(brokenTarget, name), "damaged"); File.WriteAllText(Path.Combine(brokenBackup, name), "bad backup"); throw new IOException("failed"); }
            }); } catch (UpdateTransactionException ex) { failedRecovery = !ex.Restored && ex.Message.Contains("未能恢复"); }
            check(failedRecovery, "corrupt backup is reported as failed recovery instead of success");
            string mutated = Path.Combine(root, "mutated"); Legacy(mutated); string mutatedBefore = Snapshot(mutated);
            byte[] savedNotice = File.ReadAllBytes(Path.Combine(payload, "NOTICE.txt")); bool mutationRestored = false;
            try { UpdateInstaller.ApplyPackage(payload, mutated, Path.Combine(root, "mutated-backup"), current, 1, name => { if (name == "AudioSwitch.exe") File.WriteAllText(Path.Combine(payload, "NOTICE.txt"), "changed after validation"); }); }
            catch (UpdateTransactionException ex) { mutationRestored = ex.Restored; }
            finally { File.WriteAllBytes(Path.Combine(payload, "NOTICE.txt"), savedNotice); }
            check(mutationRestored && Snapshot(mutated) == mutatedBefore, "payload mutation after validation stops transaction and restores original");
            File.WriteAllText(Path.Combine(payload, "candidate-manifest.json"), "mixed candidate");
            check(Reject(() => AppUpdate.ValidatePayload(payload, current, 1)), "candidate marker cannot be smuggled into desktop source");
            Store(root, zip, check);
        }
        private static void Store(string root, string zip, Action<bool, string> check)
        {
            string data = Path.Combine(root, "data"), work = Path.Combine(data, "updates", Guid.NewGuid().ToString("N")), payload = Path.Combine(work, "payload");
            Directory.CreateDirectory(work); File.Copy(zip, Path.Combine(work, "package.zip"));
            Directory.CreateDirectory(payload); File.WriteAllText(Path.Combine(payload, "audio-switch-panel.exe"), "partial extraction");
            var release = new UpdateRelease { Version = AppUpdate.ParseVersion(AppVersion.Number), Tag = "v" + AppVersion.Number, PackageFormat = 1, Size = new FileInfo(zip).Length, Digest = AppUpdate.Hash(zip) };
            check(AppUpdate.PrepareInDirectory(release, CancellationToken.None, null, work) == payload, "desktop verified cached ZIP resumes partial extraction without manifest or network");
            string installed = Path.Combine(root, "bridge"); Legacy(installed);
            var store = new AutomaticUpdateStore(data, Path.Combine(installed, "AudioSwitch.exe")); store.Stage(release, payload);
            var record = store.Read();
            check(record.Format == 2 && record.PackageFormat == 1 && record.Hashes.Count == 14 && !record.Attempted, "desktop cache record binds format and all 14 hashes");
            int launches = 0; string issue;
            string pendingBefore = File.ReadAllText(store.PathName);
            check(!store.TryInstall(false, (path, found, background) => launches++, out issue, false, format => { WebViewRuntime.Require(format, () => WebViewRuntime.Check(machine => null)); }) && launches == 0 && issue.Contains("WebView2"), "missing Runtime blocks automatic desktop handoff");
            check(File.ReadAllText(store.PathName) == pendingBefore && !store.Read().Attempted, "missing Runtime leaves pending bytes and retry eligibility unchanged");
            check(!store.TryInstall(false, (path, found, background) => { throw new WebViewRequirementException("WebView2 disappeared before helper start"); }, out issue, false, format => { }) && !store.Read().Attempted && launches == 0, "Runtime loss at final preflight restores automatic retry eligibility");
            check(store.TryInstall(false, (path, found, background) => { check(found.PackageFormat == 1 && found.Version == release.Version, "same-version startup handoff preserves desktop format"); launches++; }, out issue, false, format => { }) && launches == 1, "same-version bridge migration can install staged desktop update after prerequisite resolves");
            check(!store.TryInstall(false, (path, found, background) => launches++, out issue) && launches == 1, "same-version migration attempt does not repeat after interruption");
            check(store.BlockedVersion() == release.Version, "failed same-version desktop migration blocks automatic retries");
            record.Attempted = false; record.PackageFormat = 0; store.Save(record);
            check(Reject(() => store.Read()), "desktop record cannot downgrade its format while retaining desktop hashes");
            record.Format = 1; record.Attempted = true; record.Hashes = record.Hashes.Where(item => AppUpdate.Files.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value); store.Save(record);
            check(store.BlockedVersion() == null, "successfully installed bridge legacy record never blocks subsequent same-version desktop migration");
        }
        internal static void RealPackage(string candidateDirectory, Action<bool, string> check)
        {
            // Build an isolated Desktop-format fixture from verified real binaries. Never launch its tray host.
            candidateDirectory = Path.GetFullPath(candidateDirectory);
            PanelLaunch.Validate(candidateDirectory, AppVersion.Number);
            string settingsBefore = File.Exists(PreferenceStore.SettingsPath) ? AppUpdate.Hash(PreferenceStore.SettingsPath) : "absent";
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-real-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string payload = Path.Combine(root, "payload"), installed = Path.Combine(root, "installed");
            Legacy(installed); Directory.CreateDirectory(payload);
            foreach (string name in UpdatePackage.DesktopContent.Take(12)) {
                string file = Path.Combine(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.Copy(Path.Combine(candidateDirectory, name), file);
            }
            File.WriteAllText(Path.Combine(payload, "DESKTOP-README.txt"), "声间桌面格式隔离验证包。未正式发布，不要覆盖日常程序。音频功能仍需实机验收。需要系统 WebView2；不会自动安装 Runtime。");
            Manifest(payload, AppVersion.Number);
            AppUpdate.ValidatePayload(payload, AppUpdate.ParseVersion(AppVersion.Number), 1);
            check(FileIntegrity.Check(payload).Passed, "real Tauri and backend binaries pass Desktop manifest validation");
            string bridgeZip = Path.Combine(root, UpdatePackage.AssetName(AppUpdate.ParseVersion(AppVersion.Number), 0));
            using (var zip = ZipFile.Open(bridgeZip, ZipArchiveMode.Create)) foreach (string name in AppUpdate.Files) zip.CreateEntryFromFile(Path.Combine(installed, name), name);
            string desktopZip = Path.Combine(root, UpdatePackage.AssetName(AppUpdate.ParseVersion(AppVersion.Number), 1)); Zip(payload, desktopZip);
            using (var bridge = ZipFile.OpenRead(bridgeZip)) using (var desktop = ZipFile.OpenRead(desktopZip))
                check(bridge.Entries.Count == 8 && desktop.Entries.Count == 14 && !desktop.Entries.Any(e => e.FullName.Contains("candidate") || e.FullName.Contains("CANDIDATE")), "isolated dual archives preserve eight-file bridge and separate fourteen-file Desktop contract");
            string before = AppUpdate.Hash(Path.Combine(installed, "AudioSwitch.exe"));
            string backup = Path.Combine(root, "backup");
            UpdateInstaller.ApplyPackage(payload, installed, backup, AppUpdate.ParseVersion(AppVersion.Number), 1);
            check(FileIntegrity.Check(installed).Passed && AppUpdate.Hash(Path.Combine(backup, "AudioSwitch.exe")) == before, "real complete Desktop files install in isolation with verified old executable backup");
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(installed, "AudioSwitch.exe"), "--check-files --quiet") { UseShellExecute = false, CreateNoWindow = true })) {
                try { check(process.WaitForExit(10000) && process.ExitCode == 0, "installed real backend performs read-only complete Desktop check without tray startup"); }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            }
            check(settingsBefore == (File.Exists(PreferenceStore.SettingsPath) ? AppUpdate.Hash(PreferenceStore.SettingsPath) : "absent") && File.ReadAllText(Path.Combine(installed, "settings.json")) == "leave user data untouched", "real package transaction preserves active and isolated user configuration");
            File.WriteAllText(Path.Combine(root, "result.json"), Wire.Encode(new { BridgeZip = bridgeZip, DesktopZip = desktopZip, DesktopSha256 = AppUpdate.Hash(desktopZip), BridgeSha256 = AppUpdate.Hash(bridgeZip), Installed = installed, Backup = backup, SettingsUnchanged = true }));
            Console.WriteLine("Desktop package fixture: " + Path.Combine(root, "result.json"));
        }
    }
}
