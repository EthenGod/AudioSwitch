// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AudioSwitch
{
    internal sealed class IntegrityResult
    {
        internal bool Passed = true;
        internal readonly List<string> Entries = new List<string>();
        internal readonly List<string> FailedFiles = new List<string>();
        internal string Directory;
        internal string Message
        {
            get
            {
                return (Passed ? "运行必需文件检查通过。" : "运行必需文件仍有问题，暂不能继续启动。请查看下方修复结果；可解除文件占用后重试，或下载完整发布包。") +
                    "\r\n\r\n检查目录：" + Directory + "\r\n\r\n" + String.Join("\r\n", Entries) +
                    (Passed ? "" : "\r\n\r\n正式发布页：" + AppUpdate.ReleasesUrl) +
                    "\r\n\r\n说明、帮助和许可文件不影响本地运行，不要求补齐。用户设置和开机自启登记不受影响。" +
                    "\r\n检查用于发现文件缺损，不用于验证发布者身份。";
            }
        }
        internal void Fail(string message, string file = null) { Passed = false; Entries.Add(message); if (file != null) FailedFiles.Add(file); }
    }

    internal static class FileIntegrity
    {
        private const string SelfHashMarker = "AudioSwitch-Self-SHA256-v1=";
        internal static readonly string[] RequiredFiles = { "AudioSwitch.exe", "AudioSwitch.exe.config", "vendor/svcl/svcl.exe" };

        internal static bool VerifyExecutable(string path)
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 64 || stream.Length > AppUpdate.MaxPackage) return false;
                bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length) { int count = stream.Read(bytes, read, bytes.Length - read); if (count == 0) return false; read += count; }
            }
            // ASCII preserves one character per byte, so this locates the fixed resource slot.
            string ascii = System.Text.Encoding.ASCII.GetString(bytes);
            int marker = ascii.IndexOf(SelfHashMarker, StringComparison.Ordinal);
            if (marker < 0 || ascii.IndexOf(SelfHashMarker, marker + 1, StringComparison.Ordinal) >= 0) return false;
            int offset = marker + SelfHashMarker.Length;
            if (offset + 64 >= bytes.Length) return false;
            string expected = ascii.Substring(offset, 64);
            if (expected.Any(c => !Uri.IsHexDigit(c))) return false;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                sha.TransformBlock(bytes, 0, offset, bytes, 0);
                sha.TransformFinalBlock(bytes, offset + 64, bytes.Length - offset - 64);
                return String.Equals(BitConverter.ToString(sha.Hash).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static IntegrityResult Check(string directory)
        {
            var result = new IntegrityResult { Directory = directory };
            try
            {
                string exe = Path.Combine(directory, "AudioSwitch.exe");
                var assembly = AssemblyName.GetAssemblyName(exe);
                if (assembly.Name != "AudioSwitch" || assembly.ProcessorArchitecture != ProcessorArchitecture.Amd64 ||
                    assembly.Version.ToString(3) != AppVersion.Number)
                    result.Fail("版本或平台不符：AudioSwitch.exe", "AudioSwitch.exe");
                else if (!VerifyExecutable(exe)) result.Fail("内容不符或缺少校验值：AudioSwitch.exe", "AudioSwitch.exe");
                else result.Entries.Add("通过：AudioSwitch.exe（内容、名称、版本、平台）");
            }
            catch (FileNotFoundException) { result.Fail("缺失：AudioSwitch.exe", "AudioSwitch.exe"); }
            catch (DirectoryNotFoundException) { result.Fail("缺失：AudioSwitch.exe", "AudioSwitch.exe"); }
            catch (BadImageFormatException) { result.Fail("无法识别程序格式：AudioSwitch.exe", "AudioSwitch.exe"); }
            catch (Exception ex) { result.Fail("无法读取：AudioSwitch.exe（" + ex.Message + "）", "AudioSwitch.exe"); }

            try
            {
                var expected = AppUpdate.Files.Skip(1).ToArray();
                // Reject an incomplete or malformed embedded list before trusting any entries.
                string[] lines;
                using (var stream = typeof(FileIntegrity).Assembly.GetManifestResourceStream("AudioSwitch.Integrity"))
                {
                    if (stream == null) throw new InvalidDataException("程序缺少内置文件清单，请重新下载完整版本。");
                    using (var reader = new StreamReader(stream)) lines = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                }
                if (lines.Length != expected.Length + 1 || !lines[expected.Length].StartsWith(SelfHashMarker, StringComparison.Ordinal) ||
                    lines[expected.Length].Length != SelfHashMarker.Length + 64 || lines[expected.Length].Substring(SelfHashMarker.Length).Any(c => !Uri.IsHexDigit(c)))
                    throw new InvalidDataException("内置文件清单不完整。");
                var fields = lines.Take(expected.Length).Select(line => line.Split('|')).ToArray();
                for (int i = 0; i < fields.Length; i++)
                {
                    long length;
                    if (fields[i].Length != 3 || fields[i][0] != expected[i] ||
                        !Int64.TryParse(fields[i][1], NumberStyles.None, CultureInfo.InvariantCulture, out length) ||
                        length < 0 || length > AppUpdate.MaxPackage * 2 || fields[i][2].Length != 64 ||
                        fields[i][2].Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("内置文件清单无效。");
                }
                foreach (var entry in fields)
                {
                    string name = entry[0];
                    if (!RequiredFiles.Contains(name)) continue;
                    try
                    {
                        string path = Path.Combine(directory, name);
                        // The length check bounds hashing work even for an unexpectedly huge replacement.
                        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (input.Length != Int64.Parse(entry[1], CultureInfo.InvariantCulture)) result.Fail("大小不符：" + name, name);
                            else
                            {
                                string hash;
                                using (var sha = System.Security.Cryptography.SHA256.Create())
                                    hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                                if (!String.Equals(hash, entry[2], StringComparison.OrdinalIgnoreCase) ||
                                    (name == "vendor/svcl/svcl.exe" && !String.Equals(hash, SpatialAudio.HelperHash, StringComparison.OrdinalIgnoreCase)))
                                    result.Fail("内容不符：" + name, name);
                                else result.Entries.Add("通过：" + name);
                            }
                        }
                    }
                    catch (FileNotFoundException) { result.Fail("缺失：" + name, name); }
                    catch (DirectoryNotFoundException) { result.Fail("缺失：" + name, name); }
                    catch (Exception ex) { result.Fail("无法读取：" + name + "（" + ex.Message + "）", name); }
                }
            }
            catch (Exception ex) { result.Fail("检查未完成：" + ex.Message); }
            return result;
        }

        internal static bool RequireStartupFiles()
        {
            var result = Check(AppDomain.CurrentDomain.BaseDirectory);
            if (result.Passed) return true;
            using (var dialog = new IntegrityDialog(true))
            {
                dialog.ShowDialog();
                if (dialog.Restarting) return false;
                if (dialog.Passed) return true;
            }
            Environment.ExitCode = 2;
            return false;
        }
    }
}
