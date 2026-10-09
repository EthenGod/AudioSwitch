// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace AudioSwitch
{
    internal static class PanelLaunch
    {
        internal static readonly string[] Files = { "AudioSwitch.exe", "AudioSwitch.exe.config", "vendor/svcl/svcl.exe", "vendor/svcl/readme.txt", "vendor/svcl/svcl.chm", "LICENSE", "NOTICE.txt", "THIRD_PARTY.md", "audio-switch-panel.exe", "THIRD-PARTY-FRONTEND.txt", "THIRD-PARTY-RUST.txt", "THIRD-PARTY-RUST-STDLIB.html", "CANDIDATE-README.txt" };
        internal static ProcessStartInfo Select(string backend, bool prompt, int owner)
        {
            if (owner <= 0) throw new ArgumentException("后台进程无效。");
            string directory = Path.GetDirectoryName(Path.GetFullPath(backend));
            string panel = Path.Combine(directory, "audio-switch-panel.exe");
            string manifest = Path.Combine(directory, "candidate-manifest.json");
            bool modern = File.Exists(panel) || File.Exists(manifest);
            if (modern) {
                try { Validate(directory, AppVersion.Number, ValidateExecutable); }
                catch (Exception ex) { throw new InvalidOperationException("新面板文件不完整或版本不匹配，请重新解压完整候选包到独立目录后再试。" + Environment.NewLine + ex.Message, ex); }
            }
            return new ProcessStartInfo(modern ? panel : backend, (prompt ? "--prompt" : modern ? "" : "--ui") + " --owner=" + owner) {
                UseShellExecute = false, WorkingDirectory = directory
            };
        }
        internal static void Validate(string directory, string version, Action<string, string> verifyExecutable)
        {
            string path = Path.Combine(directory, "candidate-manifest.json");
            RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("缺少有效的候选清单。");
            var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 20 };
            var manifest = json.DeserializeObject(File.ReadAllText(path, new UTF8Encoding(false, true))) as Dictionary<string, object>;
            if (manifest == null || Text(manifest, "Format") != "AudioSwitch.UI.Candidate" || Number(manifest, "FormatVersion") != 1 || Text(manifest, "AppVersion") != version || Text(manifest, "Platform") != "win-x64") throw new InvalidDataException("候选清单版本或平台不匹配。");
            object rows; if (!manifest.TryGetValue("Files", out rows) || !(rows is object[])) throw new InvalidDataException("候选文件清单无效。");
            var files = (object[])rows;
            if (files.Length != Files.Length) throw new InvalidDataException("候选文件数量不匹配。");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in files) {
                var item = row as Dictionary<string, object>;
                if (item == null) throw new InvalidDataException("候选文件记录无效。");
                string name = Text(item, "Path"), expected = Text(item, "Sha256");
                long length = Number(item, "Length");
                if (!Files.Contains(name, StringComparer.Ordinal) || !seen.Add(name) || length <= 0 || length > 64L * 1024 * 1024 || expected == null || expected.Length != 64 || expected.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))) throw new InvalidDataException("候选文件路径、大小或摘要无效。");
                string file = Path.Combine(directory, name); RejectLinks(file);
                using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create()) {
                    if (stream.Length != length || BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != expected) throw new InvalidDataException("文件校验失败：" + name);
                }
            }
            verifyExecutable(Path.Combine(directory, "audio-switch-panel.exe"), version);
        }
        private static string Text(Dictionary<string, object> item, string key) { object value; return item.TryGetValue(key, out value) ? value as string : null; }
        private static long Number(Dictionary<string, object> item, string key) { object value; return item.TryGetValue(key, out value) && (value is int || value is long) ? Convert.ToInt64(value) : -1; }
        private static void RejectLinks(string path)
        {
            for (var item = new FileInfo(Path.GetFullPath(path)); item != null; item = item.Directory == null ? null : new FileInfo(item.Directory.FullName)) {
                if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) && (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("候选路径不能包含链接。");
            }
        }
        private static void ValidateExecutable(string path, string version)
        {
            using (var reader = new BinaryReader(File.OpenRead(path))) {
                if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("面板不是 Windows 程序。");
                reader.BaseStream.Position = 0x3c; int offset = reader.ReadInt32();
                if (offset < 64 || offset > reader.BaseStream.Length - 26) throw new InvalidDataException("面板程序头无效。");
                reader.BaseStream.Position = offset;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("面板不是 x64 程序。");
            }
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart + "." + info.FileMinorPart + "." + info.FileBuildPart != version) throw new InvalidDataException("面板版本与后台不同。");
        }
    }

    internal interface IFrontendWindow : IDisposable
    {
        int Id { get; }
        bool Exited { get; }
        void Activate();
        void Close();
    }
    internal sealed class FrontendWindows : IDisposable
    {
        private readonly Func<bool, ProcessStartInfo> select;
        private readonly Func<ProcessStartInfo, IFrontendWindow> start;
        private IFrontendWindow panel, prompt;
        private bool closing;
        internal FrontendWindows(string backend) : this(p => PanelLaunch.Select(backend, p, Process.GetCurrentProcess().Id), info => new NativeFrontend(Process.Start(info), Path.GetFileName(info.FileName) == "audio-switch-panel.exe", info.Arguments.Contains("--prompt"))) { }
        internal FrontendWindows(Func<bool, ProcessStartInfo> select, Func<ProcessStartInfo, IFrontendWindow> start) { this.select = select; this.start = start; }
        internal int PanelPid { get { return panel != null && !panel.Exited ? panel.Id : 0; } }
        internal int PromptPid { get { return prompt != null && !prompt.Exited ? prompt.Id : 0; } }
        internal void Show(bool isPrompt)
        {
            if (closing) return;
            var window = isPrompt ? prompt : panel;
            if (window != null && !window.Exited) { if (!isPrompt) window.Activate(); return; }
            var info = select(isPrompt); // Validation failures do not discard the prior process handle.
            if (window != null) window.Dispose();
            if (isPrompt) prompt = null; else panel = null;
            window = start(info);
            if (isPrompt) prompt = window; else panel = window;
        }
        internal void Close()
        {
            if (closing) return; closing = true;
            if (panel != null && !panel.Exited) panel.Close();
            if (prompt != null && !prompt.Exited) prompt.Close();
        }
        public void Dispose() { Close(); if (panel != null) { panel.Dispose(); panel = null; } if (prompt != null) { prompt.Dispose(); prompt = null; } }
        private sealed class NativeFrontend : IFrontendWindow
        {
            private readonly Process process;
            private readonly string title;
            private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
            [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
            [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
            [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
            [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
            [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
            internal NativeFrontend(Process process, bool modern, bool prompt) { if (process == null) throw new InvalidOperationException("无法启动界面。"); this.process = process; title = modern ? (prompt ? "声间 · 设备提示" : "声间 · 设备面板") : null; }
            public int Id { get { return process.Id; } }
            public bool Exited { get { return process.HasExited; } }
            private IntPtr WindowHandle() {
                if (title == null) { process.Refresh(); return process.MainWindowHandle; }
                // WebView2 also owns an untitled top-level window; MainWindowHandle may choose it.
                IntPtr found = IntPtr.Zero;
                EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                    uint pid; GetWindowThreadProcessId(window, out pid);
                    if (pid == process.Id) { var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity); if (text.ToString() == title) { found = window; return false; } }
                    return true;
                }, IntPtr.Zero);
                return found;
            }
            public void Activate() { var window = WindowHandle(); if (window == IntPtr.Zero) return; if (IsIconic(window)) ShowWindow(window, 9); SetForegroundWindow(window); }
            public void Close() { try { if (!process.HasExited) { var window = WindowHandle(); if (window != IntPtr.Zero) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); } } catch (InvalidOperationException) { } }
            public void Dispose() { process.Dispose(); }
        }
    }
}
