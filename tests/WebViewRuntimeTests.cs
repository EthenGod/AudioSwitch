// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AudioSwitch
{
    internal static class WebViewRuntimeTests
    {
        internal static void Run(Action<bool, string> check)
        {
            foreach (object invalid in new object[] { null, "", "0.0.0.0", 123, "123.0.0", "123.0.0.1 beta", "1.2.3.4\n", "999999999999.0.0.0" })
                check(!WebViewRuntime.Check(machine => invalid).Available, "WebView2 rejects missing or invalid pv: " + invalid);
            check(WebViewRuntime.Check(machine => machine ? "125.0.1.2" : null).Available, "WebView2 accepts machine Evergreen installation");
            check(WebViewRuntime.Check(machine => machine ? "0.0.0.0" : "126.0.1.2").Version == "126.0.1.2", "WebView2 accepts per-user installation with empty machine record");
            check(WebViewRuntime.Check(machine => machine ? "125.0.1.2" : "126.0.1.2").Version == "126.0.1.2", "WebView2 compares numeric version components");
            check(WebViewRuntime.Check(machine => { throw new UnauthorizedAccessException(); }).Unknown, "WebView2 denied reads report unknown rather than installed");
            check(WebViewRuntime.Check(machine => { if (machine) throw new UnauthorizedAccessException(); return "126.0.1.2"; }).Available, "WebView2 one valid installation suffices despite other hive denial");
            int reads = 0; bool blocked = false;
            WebViewRuntime.Require(0, () => { reads++; return new WebViewRuntimeStatus(); });
            check(reads == 0, "legacy package does not depend on WebView2");
            try { WebViewRuntime.Require(1, () => { reads++; return WebViewRuntime.Check(machine => null); }); } catch (WebViewRequirementException ex) { blocked = ex.Message.Contains(WebViewRuntime.DownloadUrl); }
            check(blocked && reads == 1, "desktop requirement blocks before handoff and explains manual installation");
            var local = WebViewRuntime.Check();
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe"), "--check-webview2") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false, true)
            })) {
                var output = process.StandardOutput.ReadToEndAsync();
                try {
                    check(process.WaitForExit(8000) && output.Wait(1000), "WebView2 readonly diagnostic exits without a backend");
                    var result = Wire.Decode<WebViewRuntimeStatus>(output.Result);
                    check(result.Available == local.Available && result.Unknown == local.Unknown && result.Message.Contains("WebView2") && process.ExitCode == (local.Available ? 0 : local.Unknown ? 3 : 2), "WebView2 diagnostic preserves UTF-8 result and exit status");
                } finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(1000); } }
            }
        }
    }
}
