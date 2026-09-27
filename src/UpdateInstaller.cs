// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    public sealed class UpdatePlan
    {
        public string Target { get; set; }
        public string Payload { get; set; }
        public string Version { get; set; }
        public int ParentPid { get; set; }
        public string ReadyEvent { get; set; }
        public Dictionary<string, string> Hashes { get; set; }
    }
    internal static class UpdateInstaller
    {
        internal static string MutexName { get { return "Local\\AudioSwitch-Update-" + Wire.Identity; } }
        internal static bool IsUpdating()
        {
            using (var mutex = new Mutex(false, MutexName))
            {
                bool acquired = false;
                try { try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; } return !acquired; }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        internal static string Quote(string value)
        {
            if (String.IsNullOrEmpty(value) || value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || value.EndsWith("\\")) throw new InvalidDataException("更新路径无效。");
            return "\"" + value + "\"";
        }
        internal static bool SamePath(string first, string second)
        { return String.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
        internal static bool Matches(Process process, string path)
        { try { return !process.HasExited && SamePath(process.MainModule.FileName, path); } catch (InvalidOperationException) { return false; } }
        internal static void RejectLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("更新目录包含链接，请把程序解压到普通文件夹后重试。");
        }
        internal static void ValidateBackupPaths(string targetDirectory, string backup)
        {
            foreach (string name in AppUpdate.Files)
            {
                foreach (string path in new[] { Path.Combine(targetDirectory, name), Path.Combine(backup, name), Path.Combine(backup, "incomplete", name) })
                {
                    if (path.Length >= 248) throw new IOException("程序目录太深，旧版备份路径会超过 Windows 的长度限制。请先把程序移到较短的目录后重试；原程序尚未退出。");
                    RejectLinks(path);
                }
            }
        }
        internal static void Launch(string payload, UpdateRelease release, int parentPid)
        {
            string work = Path.GetDirectoryName(payload);
            string target = Application.ExecutablePath;
            var plan = new UpdatePlan { Target = target, Payload = payload, Version = release.Version.ToString(3), ParentPid = parentPid,
                ReadyEvent = "Local\\AudioSwitch-Update-Ready-" + Guid.NewGuid().ToString("N"),
                Hashes = AppUpdate.Files.ToDictionary(name => name, name => AppUpdate.Hash(Path.Combine(payload, name))) };
            string helper = Path.Combine(work, "AudioSwitch.Update.exe");
            File.Copy(target, helper, false);
            File.Copy(target + ".config", helper + ".config", false);
            string planFile = Path.Combine(work, "install.json");
            File.WriteAllText(planFile, Wire.Encode(plan), new System.Text.UTF8Encoding(false));
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, plan.ReadyEvent))
            using (var helperProcess = Process.Start(new ProcessStartInfo(helper, "--apply-update " + Quote(planFile)) { UseShellExecute = false, WorkingDirectory = work }))
            {
                // Do not close the user's panel until the helper has validated its input and acquired the lock.
                var watch = Stopwatch.StartNew();
                while (!ready.WaitOne(100))
                {
                    if (helperProcess.HasExited || watch.ElapsedMilliseconds > 25000)
                        throw new IOException("更新助手未能准备就绪。当前程序仍在运行，请稍后重试。");
                }
            }
        }
        private static void ValidatePlan(UpdatePlan plan)
        {
            if (plan == null || plan.ParentPid <= 0 || plan.ReadyEvent == null || !plan.ReadyEvent.StartsWith("Local\\AudioSwitch-Update-Ready-", StringComparison.Ordinal))
                throw new InvalidDataException("更新任务无效。");
            if (Path.GetFileName(plan.Target) != "AudioSwitch.exe" || !File.Exists(plan.Target)) throw new InvalidDataException("找不到原程序。");
            if (!SamePath(plan.Payload, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload"))) throw new InvalidDataException("更新文件目录无效。");
            if (SamePath(Path.GetDirectoryName(plan.Target), plan.Payload)) throw new InvalidDataException("不能覆盖更新来源。");
            RejectLinks(plan.Target); RejectLinks(plan.Payload);
            var version = AppUpdate.ParseVersion(plan.Version);
            if (version <= AppUpdate.ParseVersion(AppVersion.Number)) throw new InvalidDataException("不能安装相同或更旧的版本。");
            AppUpdate.ValidatePayload(plan.Payload, version);
            if (plan.Hashes == null || plan.Hashes.Count != AppUpdate.Files.Length) throw new InvalidDataException("更新校验信息不完整。");
            foreach (string name in AppUpdate.Files)
            {
                string hash;
                RejectLinks(Path.Combine(plan.Payload, name)); RejectLinks(Path.Combine(Path.GetDirectoryName(plan.Target), name));
                if (!plan.Hashes.TryGetValue(name, out hash) || AppUpdate.Hash(Path.Combine(plan.Payload, name)) != hash) throw new InvalidDataException("准备好的更新文件发生变化，请重新下载。");
            }
            using (var parent = Process.GetProcessById(plan.ParentPid))
                if (!Matches(parent, plan.Target)) throw new InvalidDataException("发起更新的程序已退出或路径不一致。");
        }
        private static List<Process> RunningCopies(string target)
        {
            var result = new List<Process>();
            try
            {
                foreach (var process in Process.GetProcessesByName("AudioSwitch"))
                {
                    bool keep = false;
                    try { keep = process.SessionId == Process.GetCurrentProcess().SessionId && Matches(process, target); if (keep) result.Add(process); }
                    finally { if (!keep) process.Dispose(); }
                }
                return result;
            }
            catch { foreach (var process in result) process.Dispose(); throw; }
        }
        internal static void WaitForExit(IEnumerable<Process> processes, int timeout)
        {
            var watch = Stopwatch.StartNew();
            foreach (var process in processes)
                if (!process.WaitForExit(Math.Max(0, timeout - (int)watch.ElapsedMilliseconds)))
                    throw new IOException("程序仍被面板或音效任务占用，尚未替换文件。请关闭相关窗口后重试。");
        }
        internal static void ApplyFiles(string payload, string targetDirectory, string backup, Action<string> afterWrite = null)
        {
            RejectLinks(targetDirectory); RejectLinks(backup);
            var originals = new Dictionary<string, string>();
            var attempted = new List<string>();
            // Complete and verify every backup before touching any destination file.
            foreach (string name in AppUpdate.Files)
            {
                string target = Path.Combine(targetDirectory, name), saved = Path.Combine(backup, name);
                RejectLinks(target); RejectLinks(Path.Combine(payload, name));
                if (File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(saved));
                    string hash = AppUpdate.Hash(target); File.Copy(target, saved, false);
                    if (AppUpdate.Hash(saved) != hash) throw new IOException("原程序备份校验失败。");
                    originals.Add(name, hash);
                }
            }
            try
            {
                foreach (string name in AppUpdate.Files)
                {
                    string target = Path.Combine(targetDirectory, name), source = Path.Combine(payload, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    attempted.Add(name); // A failed Copy can already have truncated the destination.
                    File.Copy(source, target, true);
                    if (afterWrite != null) afterWrite(name);
                    if (AppUpdate.Hash(target) != AppUpdate.Hash(source)) throw new IOException("新程序写入后校验失败。");
                }
            }
            catch (Exception ex)
            {
                var failures = new List<string>();
                foreach (string name in attempted.AsEnumerable().Reverse())
                {
                    try
                    {
                        string target = Path.Combine(targetDirectory, name), saved = Path.Combine(backup, name);
                        if (originals.ContainsKey(name))
                        {
                            if (!File.Exists(target) || AppUpdate.Hash(target) != originals[name]) File.Copy(saved, target, true);
                            if (AppUpdate.Hash(target) != originals[name]) throw new IOException("恢复校验失败");
                        }
                        else if (File.Exists(target))
                        {
                            string failed = Path.Combine(backup, "incomplete", name);
                            Directory.CreateDirectory(Path.GetDirectoryName(failed)); File.Move(target, failed);
                        }
                    }
                    catch (Exception recovery) { failures.Add(name + "：" + recovery.Message); }
                }
                throw new IOException("更新失败：" + ex.Message + (failures.Count == 0 ? "\n已核验恢复原程序。" : "\n部分文件未能恢复，请退出程序后从备份恢复：\n" + String.Join("\n", failures)) + "\n备份位置：" + backup, ex);
            }
        }
        internal static void Run(string planFile)
        {
            string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result.txt");
            UpdatePlan plan = null; bool held = false, stopped = false, installed = false;
            using (var mutex = new Mutex(false, MutexName))
            {
                try
                {
                    try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("已有更新正在进行，请等待完成。");
                    if (new FileInfo(planFile).Length > 65536) throw new InvalidDataException("更新任务文件过大。");
                    plan = Wire.Decode<UpdatePlan>(File.ReadAllText(planFile)); ValidatePlan(plan);
                    string directory = Path.GetDirectoryName(plan.Target);
                    string backup = Path.Combine(directory, "update-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                    ValidateBackupPaths(directory, backup);
                    RejectLinks(backup); Directory.CreateDirectory(backup);
                    File.WriteAllText(Path.Combine(backup, "update.txt"), "Target version: " + plan.Version);
                    using (var parent = Process.GetProcessById(plan.ParentPid))
                    {
                        if (!Matches(parent, plan.Target)) throw new IOException("原程序已改变。");
                        using (var ready = EventWaitHandle.OpenExisting(plan.ReadyEvent)) ready.Set();
                        WaitForExit(new[] { parent }, 20000);
                    }
                    var running = RunningCopies(plan.Target);
                    try
                    {
                        Mutex host;
                        if (Mutex.TryOpenExisting("Local\\AudioSwitch-Host-" + Wire.Identity, out host))
                        {
                            host.Dispose();
                            var snapshot = Wire.Send(new Request { Action = "snapshot" });
                            using (var backend = Process.GetProcessById(snapshot.BackendPid))
                            {
                                if (!Matches(backend, plan.Target)) throw new IOException("另一文件夹的声间正在运行，请先退出它再更新。");
                                try {
                                    var reply = Wire.Send(new Request { Action = "exitForUpdate", UpdatePath = plan.Target });
                                    if (reply.Error != null) throw new InvalidOperationException(reply.Error);
                                }
                                catch (IOException) { } // The pipe can close as the host exits; the process wait is authoritative.
                                WaitForExit(new[] { backend }, 20000); stopped = true;
                            }
                        }
                        else stopped = true;
                        WaitForExit(running, 20000);
                        var remaining = RunningCopies(plan.Target);
                        try { WaitForExit(remaining, 20000); }
                        finally { foreach (var process in remaining) process.Dispose(); }
                    }
                    finally { foreach (var process in running) process.Dispose(); }
                    ApplyFiles(plan.Payload, directory, backup);
                    installed = true;
                    File.WriteAllText(resultPath, "已安装 " + plan.Version + "\r\n原程序备份：" + backup);
                    mutex.ReleaseMutex(); held = false;
                    // A separate event confirms the new tray host initialized; a live unresponsive process is never overwritten.
                    string readyName = "Local\\AudioSwitch-Update-Started-" + Guid.NewGuid().ToString("N");
                    using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName))
                    using (var process = Process.Start(new ProcessStartInfo(plan.Target, "--updated=" + readyName) { UseShellExecute = false, WorkingDirectory = directory }))
                    {
                        if (!ready.WaitOne(20000)) throw new IOException("新文件已安装，但未确认启动完成。请重新打开声间；如仍无法启动，可退出程序后从这里恢复旧版：\n" + backup);
                    }
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(resultPath, ex.ToString()); } catch { }
                    if (held) { mutex.ReleaseMutex(); held = false; }
                    // Restart only after a clean file transaction, or if no installation was attempted.
                    // A failed rollback requires explicit recovery, so never launch a possibly mixed installation.
                    NoticeDialog.ShowNotice(null, installed ? "更新后的启动需要检查" : "更新未完成", ex.Message + "\n\n" + (stopped ? "托盘已退出，请处理后重新打开声间。" : "原程序未被强行结束。"), true);
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
    }
}
