// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AudioSwitch
{
    public sealed class MaintenanceResult
    {
        public string Kind { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string Notes { get; set; }
        public string Directory { get; set; }
        public string[] Entries { get; set; }
    }
    internal static class PanelMaintenance
    {
        // No audio service, configuration, downloads, installer or repair calls here.
        internal static MaintenanceResult Run(string kind, CancellationToken cancel,
            Func<CancellationToken, UpdateRelease> checkUpdate = null, Func<IntegrityResult> checkFiles = null)
        {
            var result = new MaintenanceResult { Kind = kind, CurrentVersion = AppVersion.Number, Entries = new string[0] };
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (kind == "update")
                {
                    var release = (checkUpdate ?? AppUpdate.Check)(cancel);
                    cancel.ThrowIfCancellationRequested();
                    if (release == null) { result.Status = "unavailable"; result.Message = "暂未找到可用的正式版本。"; }
                    else
                    {
                        result.LatestVersion = release.Tag; result.Notes = release.Notes ?? "暂无更新说明。";
                        var current = AppUpdate.ParseVersion(AppVersion.Number);
                        result.Status = release.Version > current ? "available" : release.Version == current ? "current" : "ahead";
                        result.Message = result.Status == "available" ? "发现新版本。" : result.Status == "current" ? "当前已是最新正式版本。" : "本地版本高于最新正式版，无需更新。";
                    }
                }
                else if (kind == "files")
                {
                    var check = checkFiles == null ? FileIntegrity.Check(AppDomain.CurrentDomain.BaseDirectory) : checkFiles();
                    cancel.ThrowIfCancellationRequested();
                    result.Status = check.Passed ? "passed" : "failed"; result.Directory = check.Directory; result.Entries = check.Entries.ToArray();
                    result.Message = check.Passed ? "后台运行必需文件检查通过。" : "发现文件缺损或无法读取；本次只检查，没有修改文件。";
                }
                else throw new InvalidOperationException("不支持此检查操作。");
            }
            catch (Exception ex)
            {
                result.Status = cancel.IsCancellationRequested ? "cancelled" : "error";
                result.Message = cancel.IsCancellationRequested ? "检查已取消或超时，没有修改文件或配置。" : "检查未完成：" + ex.Message;
            }
            return result;
        }
        internal static void Main(Func<CancellationToken, UpdateRelease> checkUpdate = null)
        {
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(30000);
                try
                {
                    // The .NET Framework synchronized reader implements ReadLineAsync synchronously.
                    var reader = Console.In;
                    var input = Task.Run(() => reader.ReadLine());
                    if (!input.Wait(3000) || (input.Result != "update" && input.Result != "files"))
                        throw new InvalidOperationException("检查请求无效或超时。");
                    // The owner keeps stdin open. Cancellation or owner exit stops network work.
                    var stop = Task.Run(() => reader.ReadLine());
                    stop.ContinueWith(task => { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }, TaskScheduler.Default);
                    var result = UpdateInstaller.IsUpdating()
                        ? new MaintenanceResult { Kind = input.Result, Status = "error", Message = "正在更新声间，请完成后重新检查。", CurrentVersion = AppVersion.Number, Entries = new string[0] }
                        : Run(input.Result, cancellation.Token, checkUpdate);
                    Console.WriteLine(Wire.Encode(result));
                }
                catch (Exception ex) { Console.WriteLine(Wire.Encode(new MaintenanceResult { Status = "error", Message = "检查未完成：" + ex.Message, CurrentVersion = AppVersion.Number, Entries = new string[0] })); }
            }
        }
    }
}
