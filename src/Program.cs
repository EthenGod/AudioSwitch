// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class Program
    {
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitch");
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Contains("--dolby-worker")) { DolbyWorker.Main(); return; }
            if (args.Length == 1 && args[0] == "--panel-maintenance") { PanelMaintenance.Main(); return; }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception); NoticeDialog.ShowNotice(Form.ActiveForm, "操作未完成", e.Exception.Message, true); };
            try
            {
                if (args.Contains("--licenses")) { NoticeDialog.ShowNotice(null, "许可与第三方说明", DistributionNotices.Read()); return; }
                if (args.Length == 2 && (args[0] == "--apply-update" || args[0] == "--apply-auto-update"))
                { UpdateInstaller.Run(args[1], args[0] == "--apply-auto-update"); return; }
                if (UpdateInstaller.IsUpdating()) {
                    if (args.Contains("--check-files")) Environment.ExitCode = 3;
                    if (!args.Contains("--background") && !(args.Contains("--check-files") && args.Contains("--quiet"))) NoticeDialog.ShowNotice(null, "正在更新声间", "更新完成后会自动重新打开，请稍候。");
                    return;
                }
                if (args.Contains("--check-files"))
                {
                    if (args.Contains("--quiet")) Environment.ExitCode = FileIntegrity.Check(AppDomain.CurrentDomain.BaseDirectory).Passed ? 0 : 2;
                    else using (var dialog = new IntegrityDialog()) { dialog.ShowDialog(); Environment.ExitCode = dialog.Passed || dialog.Restarting ? 0 : 2; }
                    return; // Quiet diagnostics are read-only; interactive main repair may hand off a restart.
                }
                if (args.Contains("--ui") || args.Contains("--prompt"))
                {
                    if (!FileIntegrity.RequireStartupFiles()) return;
                    if (args.Contains("--ui")) InstanceLocation.RequireBackend(Application.ExecutablePath, Wire.Send(new Request { Action = "snapshot" }));
                    // Read without migration/writes, so the first frame already has the saved theme.
                    try { if (File.Exists(PreferenceStore.SettingsPath)) Palette.Apply(PreferenceStore.Parse(PreferenceStore.ReadFile(PreferenceStore.SettingsPath)).DarkMode); }
                    catch { } // The backend reports configuration errors; appearance must not prevent opening it.
                    bool created;
                    bool prompt = args.Contains("--prompt");
                    using (var mutex = new Mutex(true, "Local\\AudioSwitch-" + (prompt ? "Prompt-" : "UI-") + Wire.Identity, out created))
                    {
                        if (!created) return;
                        int ownerPid = 0;
                        var ownerArg = args.FirstOrDefault(a => a.StartsWith("--owner="));
                        if (ownerArg != null) Int32.TryParse(ownerArg.Substring(8), out ownerPid);
                        if (prompt)
                        {
                            // Fetch once before creating/showing the window so the first frame has actions.
                            Reply initial = null;
                            try { initial = Wire.Send(new Request { Action = "snapshot" }); } catch { }
                            if (initial != null && initial.Error == null && initial.Pending.Count == 0) return;
                            Application.Run(new DevicePrompt(ownerPid, initial));
                        }
                        else Application.Run(new Dashboard(false));
                    }
                    return;
                }
                bool first;
                using (var mutex = new Mutex(true, "Local\\AudioSwitch-Host-" + Wire.Identity, out first))
                {
                    if (!first)
                    {
                        if (!args.Contains("--background"))
                        {
                            InstanceLocation.RequireBackend(Application.ExecutablePath, Wire.Send(new Request { Action = "snapshot" }));
                            Wire.Send(new Request { Action = "show" });
                        }
                        return;
                    }
                    if (!FileIntegrity.RequireStartupFiles()) return;
                    string updateIssue = null;
                    var startupPreferences = PreferenceStore.Load(PreferenceStore.SettingsPath);
                    var updates = new AutomaticUpdateStore(DataDirectory, Application.ExecutablePath);
                    if (updates.TryInstall(args.Contains("--background"), (payload, release, background) =>
                        UpdateInstaller.LaunchAutomatic(payload, release, Process.GetCurrentProcess().Id, background), out updateIssue, !startupPreferences.AutomaticUpdatesAllowed)) return;
                    using (var host = new TrayHost(!args.Contains("--background"), updateIssue, startupPreferences))
                    {
                        var updated = args.FirstOrDefault(a => a.StartsWith("--updated=Local\\AudioSwitch-Update-Started-", StringComparison.Ordinal));
                        if (updated != null)
                        {
                            try { using (var ready = EventWaitHandle.OpenExisting(updated.Substring(10))) ready.Set(); }
                            catch (WaitHandleCannotBeOpenedException) { }
                        }
                        Application.Run(host);
                    }
                }
            }
            catch (Exception ex) { Log(ex); NoticeDialog.ShowNotice(null, "无法启动声间", ex.Message, true); }
        }
        internal static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                string file = Path.Combine(DataDirectory, "error.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256000) File.WriteAllText(file, "");
                File.AppendAllText(file, DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}
