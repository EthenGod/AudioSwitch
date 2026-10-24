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
    internal static class ReleasePackageTests
    {
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        private static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        private static int Run(string executable, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable) })) {
                if (!process.WaitForExit(15000)) { process.Kill(); process.WaitForExit(2000); throw new Exception("Read-only/extract-only process timed out"); }
                return process.ExitCode;
            }
        }
        private static string Snapshot(string directory)
        {
            return String.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => p.Substring(directory.Length) + "=" + AppUpdate.Hash(p)));
        }
        internal static int Verify(string assets)
        {
            try {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "release-verify-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var version = AppUpdate.ParseVersion(AppVersion.Number);
                string standalone = Path.Combine(assets, "AudioSwitch.exe"), zip = Path.Combine(assets, UpdatePackage.AssetName(version, UpdatePackage.Desktop));
                string[] names = new[] { "AudioSwitch.exe", Path.GetFileName(zip) };
                Require(Directory.GetFiles(assets).Select(Path.GetFileName).OrderBy(n => n).SequenceEqual(names.OrderBy(n => n)) && Directory.GetDirectories(assets).Length == 0, "exactly one full ZIP and one self-expanding EXE; no bridge asset");
                string configBefore = File.Exists(PreferenceStore.SettingsPath) ? AppUpdate.Hash(PreferenceStore.SettingsPath) : "absent";
                Require(new FileInfo(zip).Length <= AppUpdate.MaxPackage && FileIntegrity.VerifyExecutable(standalone), "full archive size and standalone integrity");
                string desktop = Path.Combine(root, "desktop");
                AppUpdate.Extract(zip, desktop, CancellationToken.None, false, UpdatePackage.Desktop);
                AppUpdate.ValidatePayload(desktop, version, UpdatePackage.Desktop);
                Require(Run(Path.Combine(desktop, "AudioSwitch.exe"), "--check-files --quiet") == 0, "full ZIP backend verifies without starting audio");
                Require(AppUpdate.Hash(standalone) != AppUpdate.Hash(Path.Combine(desktop, "AudioSwitch.exe")), "convenience EXE contains payload instead of masquerading as the backend");
                string one = Path.Combine(root, "single"); Directory.CreateDirectory(one);
                string launcher = Path.Combine(one, "AudioSwitch.exe"); File.Copy(standalone, launcher);
                Require(Run(launcher, "--check-files --quiet") == 0 && Directory.GetFiles(one).Length == 1 && Directory.GetDirectories(one).Length == 0, "standalone read-only check needs no config and writes nothing");
                Require(Run(launcher, "--extract-only") == 0, "standalone extracts without starting the real backend");
                string expanded = Path.Combine(one, "AudioSwitch");
                Require(UpdatePackage.DesktopFiles.All(n => AppUpdate.Hash(Path.Combine(desktop, n)) == AppUpdate.Hash(Path.Combine(expanded, n))), "all 14 expanded files match full ZIP bytes");
                Require(Run(Path.Combine(expanded, "AudioSwitch.exe"), "--check-files --quiet") == 0, "expanded backend complete integrity check");
                File.WriteAllText(Path.Combine(expanded, "user-note.txt"), "keep this extra file");
                string original = Snapshot(one);
                Require(Run(launcher, "--extract-only") == 0 && Snapshot(one) == original, "repeated extraction reuses complete program and preserves extra files without new work directories");
                DesktopBootstrap.ValidateExisting(expanded, new Version(0, 0, 1));
                Require(Reject(() => DesktopBootstrap.ValidateExisting(expanded, new Version(version.Major, version.Minor, version.Build + 1))), "older existing program is not silently overwritten");
                File.AppendAllText(Path.Combine(expanded, "LICENSE"), "test damage");
                string damaged = Snapshot(one);
                Require(Run(launcher, "--extract-only") == 2 && Snapshot(one) == damaged, "damaged existing program is preserved with explicit failure");
                string conflict = Path.Combine(root, "conflict"); Directory.CreateDirectory(conflict);
                File.WriteAllText(Path.Combine(conflict, "AudioSwitch"), "leave this file");
                using (var input = File.OpenRead(zip)) Require(Reject(() => DesktopBootstrap.Expand(conflict, input, version)), "file at destination blocks extraction");
                Require(Directory.GetFiles(conflict).Length == 1 && Directory.GetDirectories(conflict).Length == 0 && File.ReadAllText(Path.Combine(conflict, "AudioSwitch")) == "leave this file", "conflicting file remains untouched");
                string invalid = Path.Combine(root, "invalid.zip");
                using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create)) using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open())) writer.Write("rejected");
                string invalidRoot = Path.Combine(root, "invalid"); Directory.CreateDirectory(invalidRoot);
                using (var input = File.OpenRead(invalid)) Require(Reject(() => DesktopBootstrap.Expand(invalidRoot, input, version)), "path traversal and unexpected archive entries rejected before destination commit");
                Require(!Directory.Exists(Path.Combine(invalidRoot, "AudioSwitch")) && !File.Exists(Path.Combine(invalidRoot, "outside.txt")), "invalid package leaves no runnable installation or escaped file");
                Require(configBefore == (File.Exists(PreferenceStore.SettingsPath) ? AppUpdate.Hash(PreferenceStore.SettingsPath) : "absent"), "real user configuration unchanged");
                Console.WriteLine("Verified extraction directory: " + root);
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}