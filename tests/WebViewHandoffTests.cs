// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AudioSwitch
{
    internal static class WebViewHandoffTests
    {
        internal static int Worker(string mode, string readyName, string result)
        {
            if (mode == "missing") {
                bool allowed = UpdateInstaller.CheckRuntimeBeforeReady(UpdatePackage.Desktop, result,
                    format => WebViewRuntime.Require(format, () => WebViewRuntime.Check(machine => null)));
                return allowed ? 99 : Environment.ExitCode;
            }
            if (mode == "ready") { using (var ready = EventWaitHandle.OpenExisting(readyName)) ready.Set(); return 0; }
            if (mode == "wait") Thread.Sleep(5000);
            return 7;
        }
        private static void Handoff(string mode, string folder)
        {
            string name = "Local\\AudioSwitch-Update-Ready-" + Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, name))
            using (var worker = Process.Start(new ProcessStartInfo(typeof(WebViewHandoffTests).Assembly.Location,
                "--installer-preflight-fixture " + mode + " " + UpdateInstaller.Quote(name) + " " + UpdateInstaller.Quote(Path.Combine(folder, "result.txt"))) { UseShellExecute = false, CreateNoWindow = true })) {
                try { UpdateInstaller.WaitForReady(worker, ready, mode == "wait" ? 300 : 8000); }
                finally { if (!worker.WaitForExit(1000)) { worker.Kill(); worker.WaitForExit(2000); } }
            }
        }
        internal static void Missing()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime-handoff-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); Handoff("missing", folder);
        }
        internal static void Run(Action<bool, string> check)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "handoff-" + Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(folder);
            bool missing = false;
            try { Handoff("missing", folder); } catch (WebViewRequirementException ex) { missing = ex.Message.Contains(WebViewRuntime.DownloadUrl) && ex.Message.Contains("尚未退出原程序"); }
            check(missing && File.ReadAllText(Path.Combine(folder, "result.txt")).Contains("未检测到 Microsoft Edge WebView2"), "helper prerequisite rejection returns precise Chinese guidance across process boundary");
            foreach (string mode in new[] { "failure", "wait" }) {
                bool ordinary = false;
                try { Handoff(mode, folder); } catch (WebViewRequirementException) { } catch (IOException) { ordinary = true; }
                check(ordinary, "ordinary helper failure never becomes safe prerequisite retry: " + mode);
            }
            Handoff("ready", folder); check(true, "helper ready event still completes handoff");
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe"), config = source + ".config";
            string firstHelper, firstPlan, secondHelper, secondPlan;
            var plan = new UpdatePlan { ReadyEvent = "first", Version = AppVersion.Number };
            UpdateInstaller.PrepareHelper(folder, source, config, plan, out firstHelper, out firstPlan);
            string marker = File.ReadAllText(Path.Combine(folder, "install.json"));
            plan.ReadyEvent = "second";
            UpdateInstaller.PrepareHelper(folder, source, config, plan, out secondHelper, out secondPlan);
            check(firstHelper != secondHelper && firstPlan != secondPlan && AppUpdate.Hash(firstHelper) == AppUpdate.Hash(secondHelper), "repeated handoff creates independent helpers without overwriting prior attempt");
            check(Wire.Decode<UpdatePlan>(File.ReadAllText(firstPlan)).ReadyEvent == "first" && Wire.Decode<UpdatePlan>(File.ReadAllText(secondPlan)).ReadyEvent == "second", "repeated handoff preserves prior plan and uses the new ready event");
            check(File.ReadAllText(Path.Combine(folder, "install.json")) == marker, "retry retains original install marker blocking payload re-extraction");
        }
    }
}
