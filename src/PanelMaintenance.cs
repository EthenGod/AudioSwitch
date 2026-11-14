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
        public string[] EntryStatuses { get; set; }
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
                    if (release == null) { result.Status = "unavailable"; result.Message = L.Text("暂未找到可用的版本。"); }
                    else
                    {
                        result.LatestVersion = release.Tag; result.Notes = release.Notes ?? L.Text("暂无更新说明。");
                        var current = AppUpdate.ParseVersion(AppVersion.Number);
                        result.Status = AppUpdate.IsAvailable(release) ? "available" : release.Version == current ? "current" : "ahead";
                        result.Message = result.Status == "available" ? L.Text("发现新版本。") : result.Status == "current" ? L.Text("当前已是最新版本。") : L.Text("本地版本高于所选范围内的最新版本，无需更新。");
                    }
                }
                else if (kind == "files")
                {
                    var check = checkFiles == null ? FileIntegrity.Check(AppDomain.CurrentDomain.BaseDirectory) : checkFiles();
                    cancel.ThrowIfCancellationRequested();
                    result.Status = check.Passed ? "passed" : "failed"; result.Directory = check.Directory; result.Entries = check.Entries.ToArray(); result.EntryStatuses = check.EntryStatuses.ToArray();
                    result.Message = check.Passed ? (check.WholePackage ? L.Text("{0}文件检查通过。", check.PackageLabel) : L.Text("后台运行必需文件检查通过。")) :
                        (check.WholePackage ? L.Text("{0}检查未通过，请重新解压完整{1}包到独立目录；本次没有修改文件。", check.PackageLabel, (check.Candidate ? L.Text("候选") : L.Text("桌面"))) : L.Text("发现文件缺损或无法读取；本次只检查，没有修改文件。"));
                }
                else throw new InvalidOperationException(L.Text("不支持此检查操作。"));
            }
            catch (Exception ex)
            {
                result.Status = cancel.IsCancellationRequested ? "cancelled" : "error";
                result.Message = cancel.IsCancellationRequested ? L.Text("检查已取消或超时，没有修改文件或配置。") : L.Text("检查未完成：{0}", ex.Message);
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
                        throw new InvalidOperationException(L.Text("检查请求无效或超时。"));
                    // The owner keeps stdin open. Cancellation or owner exit stops network work.
                    var stop = Task.Run(() => reader.ReadLine());
                    stop.ContinueWith(task => { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }, TaskScheduler.Default);
                    var result = UpdateInstaller.IsUpdating()
                        ? new MaintenanceResult { Kind = input.Result, Status = "error", Message = L.Text("正在更新声间，请完成后重新检查。"), CurrentVersion = AppVersion.Number, Entries = new string[0] }
                        : Run(input.Result, cancellation.Token, checkUpdate);
                    Console.WriteLine(Wire.Encode(result));
                }
                catch (Exception ex) { Console.WriteLine(Wire.Encode(new MaintenanceResult { Status = "error", Message = L.Text("检查未完成：{0}", ex.Message), CurrentVersion = AppVersion.Number, Entries = new string[0] })); }
            }
        }
    }
}
