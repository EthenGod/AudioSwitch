// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AudioSwitch
{
    internal static class PanelUpgradeTests
    {
        private static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        private static UpdateRelease Release() { return new UpdateRelease { Version = new Version(99, 0, 0), Tag = "v99.0.0", Notes = "中文隔离测试说明", Size = 4096 }; }
        private static IntegrityResult Damaged() { var result = new IntegrityResult(); result.Fail("隔离示例缺失文件"); return result; }
        internal static void Worker(string mode)
        {
            // No network, audio service, real installer or user configuration is reachable here.
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upgrade-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            PanelUpgrade.Main(kind => new UpgradeSession(kind, root, token => Release(), token => Release(), Damaged,
                (release, token, progress) => {
                    progress(2048);
                    if (mode == "cancel") { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
                    return "isolated-test-payload";
                }, (payload, release, repair) => {
                    Thread.Sleep(200);
                    if (mode == "error") throw new IOException("隔离安装交接失败");
                    File.WriteAllText(Path.Combine(root, "accepted.txt"), "test callback only");
                }));
        }
        internal static void Run(Action<bool, string> check)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upgrade-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            int downloads = 0, installs = 0; bool repairing = false;
            Func<string, UpgradeSession> make = kind => new UpgradeSession(kind, root, token => Release(), token => Release(), Damaged,
                (release, token, progress) => { downloads++; progress(2048); return "isolated-payload"; },
                (payload, release, repair) => { installs++; repairing = repair; });
            var states = new List<string>(); Action<UpgradeResult> report = value => states.Add(value.Status);
            var job = make("update");
            check(Reject(() => job.Handle("install", CancellationToken.None, report)) && installs == 0, "upgrade cannot install before check and download");
            check(job.Handle("check", CancellationToken.None, report).CanDownload && downloads == 0, "upgrade check does not download or install");
            check(Reject(() => job.Handle("check", CancellationToken.None, report)), "upgrade rejects duplicated check");
            check(job.Handle("download", CancellationToken.None, report).Status == "ready" && downloads == 1 && installs == 0, "upgrade ready waits for separate confirmation");
            check(Reject(() => job.Handle("download", CancellationToken.None, report)) && downloads == 1, "upgrade rejects duplicated download");
            check(job.Handle("install", CancellationToken.None, report).Status == "handoff" && installs == 1 && !repairing, "upgrade only claims handoff after installer acknowledgement");
            check(Reject(() => job.Handle("install", CancellationToken.None, report)) && installs == 1, "upgrade cannot submit installation twice");
            check(states.Contains("checking") && states.Contains("downloading") && states.Contains("installing"), "upgrade reports intermediate states");
            job = make("repair");
            check(job.Handle("check", CancellationToken.None, report).Status == "repairable", "upgrade damaged files offer repair without writes");
            job.Handle("download", CancellationToken.None, report); job.Handle("install", CancellationToken.None, report);
            check(repairing && installs == 2, "upgrade repair uses separate same-version installer mode");
            job = make("update");
            check(Reject(() => job.Handle("check", new CancellationToken(true), report)) && downloads == 2, "upgrade pre-cancel does no work");
            File.WriteAllText(Path.Combine(root, "CANDIDATE-README.txt"), "candidate marker");
            job = make("update");
            check(job.Handle("check", CancellationToken.None, report).Status == "unavailable" && !job.State.CanDownload, "upgrade candidate never offers installation");
            check(Reject(() => job.Handle("download", CancellationToken.None, report)) && downloads == 2, "upgrade candidate cannot download");
            job = make("repair");
            check(job.Handle("check", CancellationToken.None, report).Status == "unavailable", "upgrade candidate cannot repair");
            var healthy = new UpgradeSession("repair", root, null, token => { throw new Exception("must not request release"); }, () => new IntegrityResult(), null, null);
            check(healthy.Handle("check", CancellationToken.None, report).Status == "passed", "upgrade healthy files never request repair release");
            var failed = new UpgradeSession("update", Path.Combine(root, "empty"), token => Release(), null, null,
                (release, token, progress) => { throw new IOException("下载摘要不符"); }, (payload, release, repair) => installs++);
            failed.Handle("check", CancellationToken.None, report);
            check(Reject(() => failed.Handle("download", CancellationToken.None, report)) && failed.State.Status != "ready", "upgrade failed download never becomes ready");
            check(Reject(() => failed.Handle("install", CancellationToken.None, report)) && installs == 2, "upgrade download failure cannot install");
            var current = new UpgradeSession("update", root, token => new UpdateRelease { Version = AppUpdate.ParseVersion(AppVersion.Number) }, null, null, null, null);
            check(current.Handle("check", CancellationToken.None, report).Status == "current" && !current.State.CanDownload, "upgrade current version never offers ordinary installation");
            foreach (var mode in new[] { "success", "cancel", "error", "eof", "idle" }) ProcessCheck(check, mode);
        }
        private static UpgradeResult Read(Process process, string status)
        {
            for (int i = 0; i < 16; i++) {
                var task = process.StandardOutput.ReadLineAsync();
                if (!task.Wait(6000) || task.Result == null) throw new IOException("Missing worker status: " + status);
                var value = Wire.Decode<UpgradeResult>(task.Result);
                if (value.Status == status) return value;
                if (value.Status == "error") throw new IOException(value.Message);
            }
            throw new IOException("Too many worker messages");
        }
        private static void ProcessCheck(Action<bool, string> check, string mode)
        {
            var start = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.Tests.exe"), "--upgrade-" + (mode == "cancel" || mode == "eof" ? "cancel" : mode == "error" ? "error" : "test") + "-worker") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false, true)
            };
            using (var process = Process.Start(start)) {
                try {
                    process.StandardInput.WriteLine("update"); process.StandardInput.Flush();
                    check(Read(process, "available").Notes == "中文隔离测试说明", "upgrade real UTF-8 process check: " + mode);
                    if (mode == "idle") {
                        process.StandardInput.Close(); check(Read(process, "cancelled").Message.Contains("没有安装"), "upgrade EOF while waiting never installs");
                    } else {
                        process.StandardInput.WriteLine("download"); process.StandardInput.Flush();
                        if (mode == "cancel" || mode == "eof") {
                            Read(process, "downloading");
                            if (mode == "eof") process.StandardInput.Close(); else { process.StandardInput.WriteLine("cancel"); process.StandardInput.Flush(); }
                            check(Read(process, "cancelled").Message.Contains("没有安装"), "upgrade cancels active download through " + mode);
                        } else {
                            check(Read(process, "ready").Message.Contains("确认"), "upgrade process waits for installation confirmation: " + mode);
                            process.StandardInput.WriteLine("install"); process.StandardInput.Flush();
                            Read(process, "installing");
                            process.StandardInput.Close();
                            check(Read(process, mode == "error" ? "error" : "handoff").Message.Contains(mode == "error" ? "勿重复提交" : "尚未确认安装完成"), "upgrade EOF after acceptance preserves handoff and honest failure: " + mode);
                        }
                    }
                    check(process.WaitForExit(6000) && process.ExitCode == 0, "upgrade worker releases process: " + mode);
                } finally { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(1000)) { process.Kill(); process.WaitForExit(1000); } } }
            }
        }
    }
}
