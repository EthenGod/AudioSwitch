// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace AudioSwitch
{
    internal sealed class RepairResult
    {
        internal IntegrityResult Check;
        internal string Payload;
        internal string Notes = "";
        internal string Message { get { return Notes + (Notes.Length == 0 ? "" : "\r\n\r\n") + Check.Message; } }
    }
    internal static class FileRepair
    {
        internal static RepairResult Run(string directory, CancellationToken cancel, Action<string> progress,
            Func<CancellationToken, string> fetchMain = null, Action<string> afterWrite = null)
        {
            var result = new RepairResult { Check = FileIntegrity.Check(directory) };
            if (result.Check.Passed) return result;
            try
            {
                cancel.ThrowIfCancellationRequested();
                // Shared with installation; never repair while another process is replacing files.
                using (var mutex = new Mutex(false, UpdateInstaller.MutexName))
                {
                    bool held = false;
                    try
                    {
                        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("正在更新或修复文件，请等待完成后重试。");
                        result.Check = FileIntegrity.Check(directory);
                        string[] names = result.Check.FailedFiles.Where(name => name != "AudioSwitch.exe").ToArray();
                        if (names.Length > 0)
                        {
                            progress("正在恢复运行必需的配套文件…");
                            string work = Path.Combine(directory, "repair-backups", Guid.NewGuid().ToString("N"));
                            string payload = Path.Combine(work, "payload"), backup = Path.Combine(work, "original");
                            UpdateInstaller.ValidateBackupPaths(directory, backup);
                            UpdateInstaller.RejectLinks(payload);
                            foreach (string name in names)
                            {
                                cancel.ThrowIfCancellationRequested();
                                string resource = name == "AudioSwitch.exe.config" ? "AudioSwitch.Repair.Config" : "AudioSwitch.Repair.Svcl";
                                string path = Path.Combine(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(path));
                                using (var input = typeof(FileRepair).Assembly.GetManifestResourceStream(resource))
                                {
                                    if (input == null) throw new InvalidDataException("内置修复文件缺失，请下载完整发布包。");
                                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                        AppUpdate.CopyBounded(input, output, AppUpdate.MaxPackage, cancel, null);
                                }
                            }
                            var staged = FileIntegrity.Check(payload);
                            if (names.Any(name => !staged.Entries.Contains("通过：" + name))) throw new InvalidDataException("内置修复文件校验失败，尚未替换原文件。");
                            cancel.ThrowIfCancellationRequested();
                            UpdateInstaller.ApplySelectedFiles(payload, directory, backup, names, name => {
                                if (afterWrite != null) afterWrite(name);
                                cancel.ThrowIfCancellationRequested();
                            });
                            result.Notes = "已恢复：" + String.Join("、", names) + "。\r\n原有文件备份：" + backup;
                            result.Check = FileIntegrity.Check(directory);
                        }
                    }
                    finally { if (held) mutex.ReleaseMutex(); }
                }
                if (result.Check.FailedFiles.Contains("AudioSwitch.exe"))
                {
                    cancel.ThrowIfCancellationRequested();
                    progress("主程序需要修复，正在下载当前版本的官方包…");
                    string payload = fetchMain == null ? AppUpdate.Prepare(AppUpdate.RepairRelease(cancel), cancel, null) : fetchMain(cancel);
                    cancel.ThrowIfCancellationRequested();
                    if (!FileIntegrity.Check(payload).Passed) throw new InvalidDataException("修复包与当前版本不匹配或文件校验失败，未替换主程序。");
                    result.Payload = payload; // Only a separate helper may replace an executable in use.
                    result.Notes += "\r\n主程序修复包已通过校验，准备重启以完成修复。";
                }
            }
            catch (Exception ex)
            {
                result.Notes += (result.Notes.Length == 0 ? "" : "\r\n") + (cancel.IsCancellationRequested ? "修复已取消或超时。\r\n" + ex.Message : "修复未完成：" + ex.Message);
                result.Check = FileIntegrity.Check(directory);
            }
            return result;
        }
    }
}
