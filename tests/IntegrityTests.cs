// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class IntegrityTests
    {
        private static string CopyPackage(string suffix)
        {
            string directory = Path.Combine(Path.GetTempPath(), "AudioSwitch-integrity-" + Guid.NewGuid().ToString("N"), suffix);
            foreach (var file in AppUpdate.Files)
            {
                string destination = Path.Combine(directory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file), destination);
            }
            return directory;
        }
        internal static void Run(Action<bool, string> check)
        {
            var original = FileIntegrity.Check(AppDomain.CurrentDomain.BaseDirectory);
            check(original.Passed && original.Entries.Count == 3, "only three runtime files determine integrity status");
            SingleExe(check);
            string directory = CopyPackage("中文 空格 & (移动位置)");
            check(FileIntegrity.Check(directory).Passed, "whole distribution remains valid after relocation");
            foreach (var file in AppUpdate.Files.Skip(1))
            {
                string path = Path.Combine(directory, file);
                byte[] bytes = File.ReadAllBytes(path);
                byte[] changed = (byte[])bytes.Clone(); changed[0] ^= 1;
                File.WriteAllBytes(path, changed);
                var result = FileIntegrity.Check(directory);
                bool required = FileIntegrity.RequiredFiles.Contains(file);
                check(required ? !result.Passed && result.Entries.Contains("内容不符：" + file) : result.Passed, "required corruption rejected and optional corruption ignored: " + file);
                File.WriteAllBytes(path, bytes);
            }
            string helper = Path.Combine(directory, "vendor/svcl/svcl.exe");
            File.Move(helper, helper + ".saved");
            string notice = Path.Combine(directory, "NOTICE.txt");
            File.WriteAllText(notice, "truncated");
            var multiple = FileIntegrity.Check(directory);
            check(!multiple.Passed && multiple.Entries.Contains("缺失：vendor/svcl/svcl.exe") && !multiple.Entries.Any(e => e.Contains("NOTICE.txt")), "missing runtime file fails but damaged documentation is ignored");
            File.Move(helper + ".saved", helper);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NOTICE.txt"), notice, true);
            using (var locked = new FileStream(helper, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var unreadable = FileIntegrity.Check(directory);
                check(!unreadable.Passed && unreadable.Entries.Any(e => e.StartsWith("无法读取：vendor/svcl/svcl.exe")), "locked dependency reports unreadable rather than missing or passed");
            }
            string exe = Path.Combine(directory, "AudioSwitch.exe");
            byte[] executableBytes = File.ReadAllBytes(exe);
            byte[] damagedExe = (byte[])executableBytes.Clone(); damagedExe[damagedExe.Length - 1] ^= 1;
            File.WriteAllBytes(exe, damagedExe);
            check(!FileIntegrity.VerifyExecutable(exe) && !FileIntegrity.Check(directory).Passed, "main executable byte corruption is detected beyond version checks");
            File.WriteAllBytes(exe, executableBytes);
            int digest = System.Text.Encoding.ASCII.GetString(executableBytes).IndexOf("AudioSwitch-Self-SHA256-v1=", StringComparison.Ordinal) + "AudioSwitch-Self-SHA256-v1=".Length;
            damagedExe = (byte[])executableBytes.Clone(); damagedExe[digest] = (byte)'G';
            File.WriteAllBytes(exe, damagedExe);
            check(!FileIntegrity.VerifyExecutable(exe), "corrupted self-checksum slot is rejected");
            File.WriteAllBytes(exe, executableBytes);
            using (var append = new StreamWriter(exe, true)) append.Write("AudioSwitch-Self-SHA256-v1=" + new string('0', 64));
            check(!FileIntegrity.VerifyExecutable(exe), "ambiguous duplicate executable checksum marker is rejected");
            File.WriteAllBytes(exe, executableBytes);
            File.Move(exe, exe + ".saved");
            check(!FileIntegrity.Check(directory).Passed, "missing main executable fails integrity check");
            File.WriteAllText(exe, "not an executable");
            check(!FileIntegrity.Check(directory).Passed, "invalid main executable format fails integrity check");
            File.Copy(exe + ".saved", exe, true);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "user data is not a package file");
            check(FileIntegrity.Check(directory).Passed, "unrelated user files and backup copies are not rejected or modified");
            check(File.ReadAllText(Path.Combine(directory, "settings.json")) == "user data is not a package file", "integrity verification preserves unrelated files");

            // The real executable diagnostics path returns without starting a tray or touching audio.
            CheckProcess(exe, 0, check, "standalone check of complete relocated package");
            File.Move(helper, helper + ".missing");
            CheckProcess(exe, 2, check, "standalone check detects incomplete package without starting audio");
            File.WriteAllText(helper, "locked-damaged-helper");
            using (var locked = new FileStream(helper, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var process = Process.Start(new ProcessStartInfo(exe, "--ui") { UseShellExecute = false }))
            {
                bool blocked = false;
                var watch = Stopwatch.StartNew();
                while (!process.HasExited && watch.ElapsedMilliseconds < 6000)
                {
                    process.Refresh();
                    if (process.MainWindowTitle == "文件检查与修复 · 声间") { blocked = true; process.CloseMainWindow(); break; }
                    Thread.Sleep(30);
                }
                bool exited = process.WaitForExit(3000);
                if (!exited && UpdateInstaller.Matches(process, exe)) { process.Kill(); process.WaitForExit(2000); }
                check(blocked && exited && process.ExitCode == 2, "real frontend startup stops with actionable integrity notice before contacting backend");
            }
            RunRepair(check, directory);
            using (var updating = new Mutex(false, UpdateInstaller.MutexName))
            {
                bool held = false;
                try {
                    try { held = updating.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new InvalidOperationException("请等待当前更新结束再运行文件检查测试。");
                    CheckProcess(exe, 3, check, "standalone check during update reports not checked rather than success");
                } finally { if (held) updating.ReleaseMutex(); }
            }
        }
        private static void SingleExe(Action<bool, string> check)
        {
            foreach (string name in AppUpdate.Files.Except(FileIntegrity.RequiredFiles))
            using (var stream = typeof(DistributionNotices).Assembly.GetManifestResourceStream("AudioSwitch.Distribution." + name))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                check(stream != null && BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == AppUpdate.Hash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name)), "embedded distribution notice/help retains exact original bytes: " + name);
            check(DistributionNotices.Read().Contains("GNU GENERAL PUBLIC LICENSE") && DistributionNotices.Read().Contains("Nir Sofer"), "embedded license viewer includes project license and third-party attribution");
            string directory = Path.Combine(Path.GetTempPath(), "AudioSwitch-single-exe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string exe = Path.Combine(directory, "AudioSwitch.exe");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe"), exe);
            using (var process = Process.Start(new ProcessStartInfo(exe, "--licenses") { UseShellExecute = false }))
            {
                bool visible = false; var watch = Stopwatch.StartNew();
                while (!process.HasExited && watch.ElapsedMilliseconds < 6000)
                {
                    process.Refresh();
                    if (process.MainWindowTitle == "许可与第三方说明 · 声间") { visible = true; process.CloseMainWindow(); break; }
                    Thread.Sleep(30);
                }
                bool exited = process.WaitForExit(3000);
                if (!exited && UpdateInstaller.Matches(process, exe)) { process.Kill(); process.WaitForExit(2000); }
                check(visible && exited && process.ExitCode == 0 && Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length == 1, "single executable displays licenses without extracting files or starting audio");
            }
            using (var process = Process.Start(new ProcessStartInfo(exe, "--check-files") { UseShellExecute = false }))
            {
                bool restored = false; var watch = Stopwatch.StartNew();
                while (!process.HasExited && watch.ElapsedMilliseconds < 8000)
                {
                    process.Refresh();
                    if (FileIntegrity.Check(directory).Passed && process.MainWindowTitle == "文件检查与修复 · 声间") { restored = true; process.CloseMainWindow(); break; }
                    Thread.Sleep(30);
                }
                bool exited = process.WaitForExit(4000);
                if (!exited && UpdateInstaller.Matches(process, exe)) { process.Kill(); process.WaitForExit(2000); }
                check(restored && exited && process.ExitCode == 0, "EXE-only first run restores config and SVCL from embedded copies without a backend");
            }
            check(AppUpdate.Files.Except(FileIntegrity.RequiredFiles).All(name => !File.Exists(Path.Combine(directory, name))), "single executable never demands optional files on disk");
        }
        private static void RunRepair(Action<bool, string> check, string directory)
        {
            string helper = Path.Combine(directory, "vendor/svcl/svcl.exe");
            foreach (var file in AppUpdate.Files.Except(FileIntegrity.RequiredFiles))
                File.Move(Path.Combine(directory, file), Path.Combine(directory, file + ".optional"));
            string config = Path.Combine(directory, "AudioSwitch.exe.config");
            File.Move(config, config + ".missing");
            int downloads = 0;
            Func<CancellationToken, string> unavailable = token => { downloads++; throw new IOException("simulated network unavailable"); };
            var repaired = FileRepair.Run(directory, CancellationToken.None, text => { }, unavailable);
            check(repaired.Check.Passed && downloads == 0, "missing config and damaged helper recover from embedded resources without network");
            check(AppUpdate.Files.Except(FileIntegrity.RequiredFiles).All(file => !File.Exists(Path.Combine(directory, file))), "optional missing files are never restored");
            check(Directory.GetFiles(Path.Combine(directory, "repair-backups"), "svcl.exe", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "locked-damaged-helper"), "damaged original preserved before repair");
            check(File.ReadAllText(Path.Combine(directory, "settings.json")) == "user data is not a package file", "repair preserves user files");
            File.WriteAllText(helper, "original-corrupt");
            repaired = FileRepair.Run(directory, CancellationToken.None, text => { }, unavailable, name => {
                File.WriteAllText(Path.Combine(directory, name), "partial-write"); throw new IOException("simulated write failure");
            });
            check(!repaired.Check.Passed && File.ReadAllText(helper) == "original-corrupt" && repaired.Notes.Contains("已核验恢复"), "partial repair failure restores and verifies original bytes");
            using (var locked = new FileStream(helper, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                repaired = FileRepair.Run(directory, CancellationToken.None, text => { }, unavailable);
                check(!repaired.Check.Passed && repaired.Notes.Contains("修复未完成"), "locked required file leaves truthful repair failure");
            }
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); repaired = FileRepair.Run(directory, cancel.Token, text => { }, unavailable);
                check(!repaired.Check.Passed && File.ReadAllText(helper) == "original-corrupt" && downloads == 0, "cancelled repair makes no writes or downloads");
            }
            string exe = Path.Combine(directory, "AudioSwitch.exe");
            File.Move(exe, exe + ".good"); File.WriteAllText(exe, "broken-exe");
            repaired = FileRepair.Run(directory, CancellationToken.None, text => { }, unavailable);
            check(!repaired.Check.Passed && downloads == 1 && repaired.Notes.Contains("network unavailable") && File.ReadAllText(exe) == "broken-exe", "unavailable official main repair reports failure and preserves damaged executable");
            repaired = FileRepair.Run(directory, CancellationToken.None, text => { }, token => AppDomain.CurrentDomain.BaseDirectory);
            check(repaired.Payload != null && !repaired.Check.Passed && File.ReadAllText(exe) == "broken-exe", "verified main repair is staged for helper without overwriting a running executable");
            repaired = FileRepair.Run(directory, CancellationToken.None, text => { }, token => directory);
            check(repaired.Payload == null && !repaired.Check.Passed, "unverified main repair payload is rejected");
            File.Copy(exe + ".good", exe, true);
            File.Move(helper, helper + ".before-process");
            using (var process = Process.Start(new ProcessStartInfo(exe, "--check-files") { UseShellExecute = false }))
            {
                bool fixedFile = false; var watch = Stopwatch.StartNew();
                while (!process.HasExited && watch.ElapsedMilliseconds < 8000)
                {
                    process.Refresh();
                    if (File.Exists(helper) && FileIntegrity.Check(directory).Passed && process.MainWindowTitle == "文件检查与修复 · 声间")
                    { fixedFile = true; process.CloseMainWindow(); break; }
                    Thread.Sleep(30);
                }
                bool exited = process.WaitForExit(4000);
                if (!exited && UpdateInstaller.Matches(process, exe)) { process.Kill(); process.WaitForExit(2000); }
                check(fixedFile && exited && process.ExitCode == 0, "real standalone repair restores missing helper and exits successfully without audio backend");
            }
            UpdateInstaller.ValidateVersion(AppUpdate.ParseVersion(AppVersion.Number), true);
            bool rejected = false;
            try { UpdateInstaller.ValidateVersion(new Version(99, 0, 0), true); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "repair installer rejects a different version");
            rejected = false;
            try { UpdateInstaller.ValidateVersion(AppUpdate.ParseVersion(AppVersion.Number), false); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "normal updater still rejects same-version installation");
            int pages = 0;
            var release = AppUpdate.RepairRelease(CancellationToken.None, (url, token) => {
                pages++; string tag = "v" + AppVersion.Number;
                check(url == AppUpdate.ReleasesUrl + "/expanded_assets/" + tag, "main repair targets exact current version rather than latest");
                return new UpdatePage { Address = new Uri(url), Text = "<li><a href='/EthenGod/AudioSwitch/releases/download/" + tag + "/AudioSwitch-" + tag + "-win-x64.zip'>file</a><clipboard-copy value='sha256:" + new string('a', 64) + "'></clipboard-copy></li>" };
            });
            check(pages == 1 && release.Version == AppUpdate.ParseVersion(AppVersion.Number) && release.Digest.Length == 64, "repair release requires official asset digest even for same version");
        }
        private static void CheckProcess(string exe, int exitCode, Action<bool, string> check, string name)
        {
            using (var process = Process.Start(new ProcessStartInfo(exe, "--check-files --quiet") { UseShellExecute = false, CreateNoWindow = true }))
            {
                bool exited = process.WaitForExit(8000);
                if (!exited && UpdateInstaller.Matches(process, exe)) { process.Kill(); process.WaitForExit(2000); }
                check(exited && process.ExitCode == exitCode, name);
            }
        }
        internal static void Render(Action<bool, string> check)
        {
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(output);
            foreach (bool dark in new[] { false, true })
            {
                Palette.Apply(dark);
                foreach (bool good in new[] { false, true })
                {
                    var result = good ? FileIntegrity.Check(AppDomain.CurrentDomain.BaseDirectory) : FileIntegrity.Check(Path.Combine(output, "示例 未安装目录"));
                    using (var form = new IntegrityDialog(false, true))
                    {
                        form.RenderResult(new RepairResult { Check = result, Notes = good ? "已自动恢复：vendor/svcl/svcl.exe。原文件已备份。" : "修复未完成：文件被其他程序占用，请关闭相关程序后重试。" });
                        form.Show(); Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(output, "integrity-" + (good ? "pass-" : "fail-") + (dark ? "dark" : "light") + ".png"));
                        }
                        check(form.Controls.OfType<Button>().All(b => form.ClientRectangle.Contains(b.Bounds)), "integrity result buttons fit " + good + "/" + dark);
                        form.Close();
                    }
                }
            }
            Palette.Apply(false);
        }
    }
}
