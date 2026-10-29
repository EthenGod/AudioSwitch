// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AudioSwitch
{
    public sealed class WebViewRuntimeStatus
    {
        public bool Available { get; set; }
        public bool Unknown { get; set; }
        public string Version { get; set; }
        public string Message { get; set; }
    }
    internal sealed class WebViewRequirementException : InvalidOperationException
    { internal WebViewRequirementException(string message) : base(message) { } }
    internal static class WebViewRuntime
    {
        internal const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
        private const string Key = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        // Official Evergreen detection: HKLM 32-bit view and HKCU native view, pv REG_SZ > 0.0.0.0.
        // https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
        private static object ReadVersion(bool machine)
        {
            using (var hive = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, machine ? RegistryView.Registry32 : RegistryView.Registry64))
            using (var key = hive.OpenSubKey(Key, false)) {
                if (key == null) return null;
                var value = key.GetValue("pv", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return value != null && key.GetValueKind("pv") == RegistryValueKind.String ? value : null;
            }
        }
        internal static WebViewRuntimeStatus Check(Func<bool, object> read = null)
        {
            read = read ?? ReadVersion; Version best = null; bool unknown = false;
            foreach (bool machine in new[] { false, true }) {
                try {
                    string text = read(machine) as string; Version version;
                    if (text != null && Regex.IsMatch(text, @"\A[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+\z") && Version.TryParse(text, out version) && version > new Version(0, 0, 0, 0) && (best == null || version > best)) best = version;
                } catch (IOException) { unknown = true; }
                catch (UnauthorizedAccessException) { unknown = true; }
                catch (System.Security.SecurityException) { unknown = true; }
            }
            bool available = best != null;
            return new WebViewRuntimeStatus { Available = available, Unknown = !available && unknown, Version = available ? best.ToString() : null,
                Message = available ? "已检测到 Microsoft Edge WebView2 Runtime " + best + "。" :
                    (unknown ? "无法确认 WebView2 Runtime 是否可用。" : "未检测到 Microsoft Edge WebView2 Runtime。") +
                    "新界面需要此组件，请从微软官网下载并安装 Evergreen Runtime 后重试；如已安装，请检查系统读取权限。\r\n" + DownloadUrl +
                    "\r\n本次不会安装组件或替换程序文件。" };
        }
        internal static void Require(int format) { Require(format, () => Check()); }
        internal static void Require(int format, Func<WebViewRuntimeStatus> check)
        {
            UpdatePackage.Files(format);
            if (format == UpdatePackage.Desktop) { var status = check(); if (!status.Available) throw new WebViewRequirementException(status.Message); }
        }
        internal static void RequireForDirectory(string directory)
        { if (PanelLaunch.IsModern(directory)) Require(UpdatePackage.Desktop); }
        internal static void Main()
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            var status = Check(); Console.WriteLine(Wire.Encode(status)); Environment.ExitCode = status.Available ? 0 : status.Unknown ? 3 : 2;
        }
    }
}
