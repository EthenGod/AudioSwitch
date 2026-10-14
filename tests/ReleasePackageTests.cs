// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AudioSwitch
{
    internal static class ReleasePackageTests
    {
        internal static int Verify(string assets)
        {
            try {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "release-verify-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var version = AppUpdate.ParseVersion(AppVersion.Number);
                string standalone = Path.Combine(assets, "AudioSwitch.exe");
                string[] names = new[] { "AudioSwitch.exe", UpdatePackage.AssetName(version, 0), UpdatePackage.AssetName(version, 1) };
                if (!Directory.GetFiles(assets).Select(Path.GetFileName).OrderBy(n => n).SequenceEqual(names.OrderBy(n => n)) || Directory.GetDirectories(assets).Length != 0) throw new Exception("Unexpected upload assets");
                foreach (int format in new[] { 0, 1 }) {
                    string zip = Path.Combine(assets, UpdatePackage.AssetName(version, format)), target = Path.Combine(root, format == 0 ? "bridge" : "desktop");
                    if (new FileInfo(zip).Length > AppUpdate.MaxPackage) throw new Exception("Release exceeds download limit");
                    AppUpdate.Extract(zip, target, CancellationToken.None, false, format);
                    AppUpdate.ValidatePayload(target, version, format);
                    if (AppUpdate.Hash(standalone) != AppUpdate.Hash(Path.Combine(target, "AudioSwitch.exe"))) throw new Exception("Standalone/ZIP executable mismatch");
                    using (var process = Process.Start(new ProcessStartInfo(Path.Combine(target, "AudioSwitch.exe"), "--check-files --quiet") { UseShellExecute = false, CreateNoWindow = true })) {
                        if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Read-only integrity check timed out"); }
                        if (process.ExitCode != 0) throw new Exception("Extracted package integrity check failed");
                    }
                    Console.WriteLine("PASS release archive: " + Path.GetFileName(zip) + " (" + UpdatePackage.Files(format).Length + " files)");
                }
                Console.WriteLine("Verified extraction directory: " + root);
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
