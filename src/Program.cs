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
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception); NoticeDialog.ShowNotice(Form.ActiveForm, "操作未完成", e.Exception.Message, true); };
            try
            {
                if (args.Contains("--ui") || args.Contains("--prompt"))
                {
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
                    if (!first) { Wire.Send(new Request { Action = "show" }); return; }
                    using (var host = new TrayHost(!args.Contains("--background"))) Application.Run(host);
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
