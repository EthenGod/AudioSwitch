// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AudioSwitch
{
    internal static class PanelMaintenanceTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var version = AppUpdate.ParseVersion(AppVersion.Number);
            foreach (var pair in new[] { new { Version = version, Status = "current" }, new { Version = new Version(99, 0, 0), Status = "available" }, new { Version = new Version(0, 0, 1), Status = "ahead" } })
            {
                var result = PanelMaintenance.Run("update", CancellationToken.None, token => new UpdateRelease { Version = pair.Version, Tag = "v" + pair.Version, Notes = "中文版本说明" });
                check(result.Status == pair.Status && result.Notes == "中文版本说明", "maintenance classifies update " + pair.Status);
            }
            check(PanelMaintenance.Run("update", CancellationToken.None, token => null).Status == "unavailable", "maintenance distinguishes no release");
            check(PanelMaintenance.Run("update", CancellationToken.None, token => { throw new IOException("网络不可用"); }).Message.Contains("网络不可用"), "maintenance preserves Chinese network errors");
            int calls = 0;
            check(PanelMaintenance.Run("files", new CancellationToken(true), null, () => { calls++; return null; }).Status == "cancelled" && calls == 0, "cancelled maintenance never starts file check");
            var damaged = new IntegrityResult { Directory = "隔离测试目录" }; damaged.Fail("缺失：AudioSwitch.exe.config", "AudioSwitch.exe.config");
            var failure = PanelMaintenance.Run("files", CancellationToken.None, null, () => damaged);
            check(failure.Status == "failed" && failure.Entries.Length == 1 && failure.Message.Contains("没有修改"), "maintenance reports missing files without claiming repair");
            check(PanelMaintenance.Run("repair", CancellationToken.None).Status == "error", "maintenance rejects repair command");
            ProcessCheck(check, "files", false);
            ProcessCheck(check, "unsupported", false);
            ProcessCheck(check, "update", true);
        }
        private static void ProcessCheck(Action<bool, string> check, string input, bool cancel)
        {
            var executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cancel ? "AudioSwitch.Tests.exe" : "AudioSwitch.exe");
            using (var process = Process.Start(new ProcessStartInfo(executable, cancel ? "--maintenance-test-worker" : "--panel-maintenance") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false, true) }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                process.StandardInput.WriteLine(input); process.StandardInput.Flush();
                if (cancel) process.StandardInput.Close();
                try
                {
                    check(process.WaitForExit(8000) && output.Wait(1000), "maintenance worker exits: " + input);
                    var result = Wire.Decode<MaintenanceResult>(output.Result);
                    check(cancel ? result.Status == "cancelled" && result.Message.Contains("取消") : input == "files" ? result.Status == "passed" && result.Directory == AppDomain.CurrentDomain.BaseDirectory : result.Status == "error" && result.Message.Contains("请求无效"), "maintenance real worker returns UTF-8 result: " + input);
                }
                finally { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(2000)) { process.Kill(); process.WaitForExit(1000); } } }
            }
        }
    }
}
