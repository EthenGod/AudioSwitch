// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    // A separate build entry point. The extracted backend still starts at Program.Main.
    internal static class DesktopBootstrap
    {
        internal const string ResourceName = "AudioSwitch.DesktopPayload";

        [STAThread]
        private static void Main(string[] args)
        {
            bool extractOnly = args.Length == 1 && args[0] == "--extract-only";
            bool checkOnly = args.Length == 2 && args[0] == "--check-files" && args[1] == "--quiet";
            try {
                if (!checkOnly && !extractOnly && args.Length != 0 && !(args.Length == 1 && args[0] == "--background"))
                    throw new InvalidOperationException("便捷展开版仅支持直接运行或 --background。其他操作请使用 AudioSwitch 文件夹内的主程序。");
                if (!FileIntegrity.VerifyExecutable(Application.ExecutablePath)) throw new InvalidDataException("便捷展开版校验失败，请重新下载。");
                using (var archive = typeof(DesktopBootstrap).Assembly.GetManifestResourceStream(ResourceName)) {
                    if (archive == null) throw new InvalidDataException("缺少内置完整程序包，请重新下载。");
                    if (checkOnly) return; // Read-only: no directories, extraction or audio host.
                    if (UpdateInstaller.IsUpdating()) throw new IOException("声间正在更新，请等待更新结束后再打开。");
                    string target = Expand(AppDomain.CurrentDomain.BaseDirectory, archive, AppUpdate.ParseVersion(AppVersion.Number));
                    if (!extractOnly) {
                        using (var process = Process.Start(new ProcessStartInfo(Path.Combine(target, "AudioSwitch.exe"), args.Length == 1 ? "--background" : "") {
                            UseShellExecute = false, WorkingDirectory = target
                        })) { }
                    }
                }
            }
            catch (Exception ex) {
                Environment.ExitCode = 2;
                if (extractOnly || checkOnly) Console.Error.WriteLine(ex.Message);
                else {
                    Application.EnableVisualStyles();
                    NoticeDialog.ShowNotice(null, "无法展开或打开声间", ex.Message + "\r\n已有程序、配置和备份不会被覆盖。可将便捷展开版放到新的可写文件夹重试，或使用完整 ZIP。", true);
                }
            }
        }

        internal static string Expand(string baseDirectory, Stream archive, Version minimumVersion)
        {
            string root = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            UpdateInstaller.RejectLinks(root);
            string target = Path.Combine(root, "AudioSwitch");
            string key;
            using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "");
            using (var mutex = new Mutex(false, "Local\\AudioSwitch-Expand-" + Wire.Identity + "-" + key)) {
                bool held = false;
                try {
                    try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("另一个声间正在展开，请稍后重试。");
                    UpdateInstaller.RejectLinks(target);
                    if (File.Exists(target)) throw new IOException("旁边的 AudioSwitch 是文件，无法创建程序文件夹。");
                    if (Directory.Exists(target)) {
                        ValidateExisting(target, minimumVersion);
                        return target; // Reuse a complete same/newer installation; never undo an update.
                    }
                    string work = Path.Combine(root, "AudioSwitch-unpack-" + Guid.NewGuid().ToString("N"));
                    string payload = Path.Combine(work, "payload"), zip = Path.Combine(work, "package.zip");
                    // Both sides of the single directory move are fixed descendants of this executable's directory.
                    if (!Path.GetFullPath(payload).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(target).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("展开目录越界。");
                    UpdateInstaller.RejectLinks(work);
                    Directory.CreateDirectory(work);
                    try {
                        using (var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                            var buffer = new byte[81920]; long total = 0; int count;
                            while ((count = archive.Read(buffer, 0, buffer.Length)) > 0) {
                                total += count;
                                if (total > AppUpdate.MaxPackage) throw new InvalidDataException("内置程序包超过大小限制。");
                                output.Write(buffer, 0, count);
                            }
                            if (total == 0) throw new InvalidDataException("内置程序包为空。");
                        }
                        AppUpdate.Extract(zip, payload, CancellationToken.None, false, UpdatePackage.Desktop);
                        AppUpdate.ValidatePayload(payload, minimumVersion, UpdatePackage.Desktop);
                        ValidateExisting(payload, minimumVersion);
                        UpdateInstaller.RejectLinks(payload); UpdateInstaller.RejectLinks(target);
                        Directory.Move(payload, target); // No overwrite, including if another writer created target.
                        return target;
                    }
                    catch (Exception ex) { throw new IOException("展开未完成，未替换已有程序。保留的展开文件：" + work + "\r\n" + ex.Message, ex); }
                    // Preserve the archive/work directory for recovery; never bulk-delete output.
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }

        internal static void ValidateExisting(string directory, Version minimumVersion)
        {
            UpdateInstaller.RejectLinks(directory);
            foreach (string name in UpdatePackage.DesktopFiles) UpdateInstaller.RejectLinks(Path.Combine(directory, name));
            if (!UpdatePackage.IsDesktop(directory)) throw new IOException("已有 AudioSwitch 文件夹不是完整桌面程序，不会覆盖它。");
            string exe = Path.Combine(directory, "AudioSwitch.exe");
            var assembly = AssemblyName.GetAssemblyName(exe);
            var version = new Version(assembly.Version.ToString(3));
            if (assembly.Name != "AudioSwitch" || assembly.ProcessorArchitecture != ProcessorArchitecture.Amd64 || version < minimumVersion)
                throw new IOException("已有程序版本较旧或格式不正确。请通过原程序检查更新，或在新的文件夹展开。");
            AppUpdate.ValidatePayload(directory, version, UpdatePackage.Desktop);
            if (!FileIntegrity.VerifyExecutable(exe)) throw new InvalidDataException("已有主程序内容校验失败，请使用完整包修复。");
        }
    }
}
