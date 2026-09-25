// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class TrayHost : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly AudioService audio;
        private readonly ArrivalTracker tracker;
        private readonly NotifyIcon tray;
        private readonly DeviceEventBatch debounce;
        private readonly PipeServer server;
        private readonly DolbyQueue dolby;
        private Preferences preferences;
        private Process frontend;
        private Process promptFrontend;
        private bool exiting;
        private bool disposed;
        private string audioError;
        private string priorityError;
        private readonly Dictionary<string, string> presetWarnings = new Dictionary<string, string>();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        internal TrayHost(bool show)
        {
            var handle = dispatcher.Handle;
            preferences = LoadPreferences();
            Palette.Apply(preferences.DarkMode);
            audio = new AudioService();
            tracker = new ArrivalTracker(audio.Read());
            dolby = new DolbyQueue(action => dispatcher.BeginInvoke(action), DolbyWorker.Run, (id, result) => {
                string key = "dolby:" + id;
                if (result.Error == null && result.Warning == null) presetWarnings.Remove(key);
                else {
                    var device = tracker.Current.Devices.FirstOrDefault(d => d.Id == id);
                    presetWarnings[key] = (device == null ? "设备" : device.Name) + "：" + (result.Error ?? result.Warning);
                }
            });
            tray = new NotifyIcon { Text = "声间 · 音频设备管理", Icon = AppIcon.Create(SystemInformation.SmallIconSize.Width), Visible = true };
            tray.DoubleClick += delegate { ShowFrontend(false); };
            tray.BalloonTipClicked += delegate { ShowFrontend(true); };
            var menu = new ThemedMenu();
            menu.Opening += delegate { BuildMenu(menu); };
            tray.ContextMenuStrip = menu;
            debounce = new DeviceEventBatch(() => RefreshAudio(true, true));
            audio.Listen(delegate {
                if (exiting) return;
                // Bound the wait from the first event; later callbacks must not postpone it.
                try { dispatcher.BeginInvoke((Action)delegate { if (!exiting) debounce.Signal(); }); }
                catch (InvalidOperationException) { }
            });
            server = new PipeServer(request => (Reply)dispatcher.Invoke((Func<Reply>)(() => Handle(request))));
            try
            {
                bool hadOrder = preferences.DeviceOrder.Count > 0;
                if (DevicePriority.Remember(preferences, tracker.Current)) SavePreferences();
                if (hadOrder) ApplyPriority(DeviceAutomation.StartupTargets(tracker.Current, preferences));
            }
            catch (Exception ex) { priorityError = "初始化设备优先级失败：" + ex.Message; Program.Log(ex); }
            ObserveDolby(tracker.Current);
            if (show) dispatcher.BeginInvoke((Action)(() => ShowFrontend(false)));
        }
        private void RefreshAudio(bool notify, bool automatic = false)
        {
            try
            {
                var next = audio.Read();
                if (DevicePriority.Remember(preferences, next)) SavePreferences();
                bool arrival;
                audioError = null;
                if (automatic)
                {
                    bool handled;
                    var errors = DeviceAutomation.Process(tracker, next, preferences, target => SwitchTo(target.Id, DeviceAutomation.Rule(preferences, target.Id) != DeviceRule.Normal), out arrival, out handled);
                    if (handled)
                    {
                        ReportAutomaticErrors(errors);
                        UpdateAfterAutomation();
                    }
                }
                else arrival = tracker.Update(next, preferences.AskOnConnect, true, preferences.IncludeCommunications);
                ObserveDolby(tracker.Current);
                if (arrival && notify)
                {
                    ShowFrontend(true);
                }
            }
            catch (Exception ex) { audioError = "更新音频设备失败：" + ex.Message; Program.Log(ex); }
        }
        private void ApplyPriority(System.Collections.Generic.List<Endpoint> targets)
        {
            if (targets.Count == 0) return;
            var errors = new System.Collections.Generic.List<string>();
            foreach (var target in targets)
            {
                try { SwitchTo(target.Id, DeviceAutomation.Rule(preferences, target.Id) != DeviceRule.Normal); }
                catch (Exception ex) { errors.Add(target.Name + "：" + ex.Message); Program.Log(ex); }
            }
            ReportAutomaticErrors(errors);
            UpdateAfterAutomation();
        }
        private void ReportAutomaticErrors(List<string> errors)
        {
            priorityError = errors.Count == 0 ? null : "自动选择设备未完成。" + String.Join("；", errors);
            if (priorityError != null)
            {
                Program.Log(new InvalidOperationException(priorityError));
                tray.ShowBalloonTip(5000, "自动切换未完成", priorityError, ToolTipIcon.Warning);
            }
        }
        private void UpdateAfterAutomation()
        {
            var actual = audio.Read();
            // Do not consume a second hot-plug that happened while presets were being applied.
            if (new System.Collections.Generic.HashSet<string>(tracker.Current.Devices.Select(d => d.Id)).SetEquals(actual.Devices.Select(d => d.Id)))
                tracker.Update(actual, preferences.AskOnConnect, true, preferences.IncludeCommunications);
            else debounce.Signal();
            ObserveDolby(actual);
        }
        private void ObserveDolby(AudioState state, bool force = false)
        {
            string id = state.Default(0, 1); DeviceProfile profile;
            dolby.Observe(id, id != null && preferences.DeviceProfiles.TryGetValue(id, out profile) ? profile.Dolby : null, force);
        }
        private void BuildMenu(ContextMenuStrip menu)
        {
            Palette.Apply(preferences.DarkMode);
            while (menu.Items.Count > 0) { var item = menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
            menu.Items.Add(new TrayMenuHeading());
            menu.Items.Add("打开管理面板", null, delegate { ShowFrontend(false); });
            if (tracker.Pending.Count > 0) menu.Items.Add("有设备变化待处理…", null, delegate { ShowFrontend(true); });
            menu.Items.Add(new ToolStripSeparator());
            foreach (int flow in new[] { 0, 1 })
            {
                var current = tracker.Current.Devices.FirstOrDefault(d => d.Id == tracker.Current.Default(flow, 1));
                var group = new TrayDeviceGroup(flow == 0 ? "声音输出" : "麦克风输入", current == null ? "暂无默认设备" : current.Name) { DropDown = new ThemedMenu() };
                foreach (var device in DevicePriority.Ordered(tracker.Current.Devices, preferences, tracker.Current, flow))
                {
                    var captured = device;
                    var item = new ToolStripMenuItem(device.Name) { Checked = tracker.Current.Default(flow, 1) == device.Id };
                    item.Click += delegate {
                        var result = Handle(new Request { Action = "switch", DeviceId = captured.Id });
                        if (result.Error != null) tray.ShowBalloonTip(5000, "切换未完成", result.Error, ToolTipIcon.Warning);
                    };
                    group.DropDownItems.Add(item);
                }
                if (group.DropDownItems.Count == 0) group.DropDownItems.Add("暂无可用设备").Enabled = false;
                menu.Items.Add(group);
            }
            menu.Items.Add(new ToolStripSeparator());
            var priority = new ToolStripMenuItem("按设备优先级自动选择") { Checked = preferences.UseDevicePriority };
            priority.Click += delegate {
                var result = Handle(new Request { Action = "priority", Value = !preferences.UseDevicePriority });
                if (result.Error != null) tray.ShowBalloonTip(5000, "设备优先级", result.Error, ToolTipIcon.Warning);
            };
            menu.Items.Add(priority);
            menu.Items.Add("退出", null, delegate { ExitThread(); });
        }
        private Reply Handle(Request request)
        {
            string error = null;
            DeviceSettingsInfo settings = null;
            string backupPath = null;
            bool preferencesSaved = false;
            var beforePreferences = Wire.Decode<Preferences>(Wire.Encode(preferences));
            try
            {
                switch (request.Action)
                {
                    case "snapshot": break;
                    case "darkMode": preferences.DarkMode = request.Value; SavePreferences(); break;
                    case "exportSettings": return new Reply { ConfigurationJson = PreferenceStore.Export(preferences) };
                    case "importSettings":
                        preferences = PreferenceStore.Import(PreferenceStore.SettingsPath, request.ConfigurationJson, preferences, out backupPath);
                        preferencesSaved = true;
                        tracker.Pending.Clear(); presetWarnings.Clear(); priorityError = null; audioError = null;
                        dolby.Cancel();
                        break;
                    case "dismissWarning":
                        // A stale close click must not discard a newer warning.
                        if (request.Message != null && request.Message == String.Join("；", presetWarnings.Values)) presetWarnings.Clear();
                        break;
                    case "refresh": RefreshAudio(true, true); break;
                    case "show": ShowFrontend(false); break;
                    case "deviceSettings": settings = ReadDeviceSettings(request.DeviceId); break;
                    case "saveDeviceSettings":
                        var info = ReadDeviceSettings(request.DeviceId);
                        if (request.DeviceRule.HasValue && !Enum.IsDefined(typeof(DeviceRule), request.DeviceRule.Value))
                            throw new InvalidOperationException("无效的白名单规则。");
                        DeviceProfiles.Validate(request.Profile, info.Device.Flow);
                        // Saving a valid preset does not require its driver to be readable/available.
                        // Actual application performs capability checks and rollback in DeviceProfiles.Apply.
                        preferences.DeviceProfiles[request.DeviceId] = Wire.Decode<DeviceProfile>(Wire.Encode(request.Profile));
                        if (request.DeviceRule.HasValue)
                        {
                            if (request.DeviceRule.Value == DeviceRule.Normal) preferences.DeviceRules.Remove(request.DeviceId);
                            else preferences.DeviceRules[request.DeviceId] = request.DeviceRule.Value;
                        }
                        SavePreferences(); preferencesSaved = true;
                        presetWarnings.Remove(request.DeviceId);
                        presetWarnings.Remove("dolby:" + request.DeviceId);
                        if (info.Device.Flow == 0 && tracker.Current.Default(0, 1) == request.DeviceId) dolby.Cancel();
                        if (request.Value)
                        {
                            SwitchTo(request.DeviceId);
                            tracker.Pending.RemoveAll(p => p.Flow == info.Device.Flow);
                            RefreshAudio(false);
                        }
                        settings = ReadDeviceSettings(request.DeviceId);
                        break;
                    case "switch":
                        RefreshAudio(false);
                        SwitchTo(request.DeviceId);
                        var device = tracker.Current.Devices.First(d => d.Id == request.DeviceId);
                        tracker.Pending.RemoveAll(p => p.Flow == device.Flow);
                        RefreshAudio(false);
                        break;
                    case "new":
                    case "alternative":
                    case "old":
                        RefreshAudio(false);
                        var pending = tracker.Pending.FirstOrDefault(p => p.Token == request.Token);
                        if (pending == null) throw new InvalidOperationException("设备状态已变化，请重新选择。");
                        if (request.Action == "alternative")
                        {
                            if (!tracker.Current.Devices.Any(d => d.Id == request.DeviceId && d.Flow == pending.Flow))
                                throw new InvalidOperationException("所选设备已断开，请重新选择在线设备。");
                            SwitchTo(request.DeviceId);
                        }
                        else if (request.Action == "new")
                        {
                            if (!pending.NewDevices.Any(d => d.Id == request.DeviceId)) throw new InvalidOperationException("新设备已断开。");
                            SwitchTo(request.DeviceId);
                        }
                        else RestorePrevious(pending);
                        tracker.Dismiss(request.Token);
                        RefreshAudio(false);
                        break;
                    case "later": tracker.Dismiss(request.Token); break;
                    case "priority":
                        RefreshAudio(false);
                        preferences.UseDevicePriority = request.Value;
                        SavePreferences(); preferencesSaved = true;
                        priorityError = null;
                        if (request.Value) ApplyPriority(DevicePriority.Targets(tracker.Current, tracker.Current, preferences, -1));
                        break;
                    case "deviceOrder":
                        RefreshAudio(false);
                        DevicePriority.Reorder(preferences, request.Flow, request.DeviceOrder);
                        SavePreferences(); preferencesSaved = true;
                        ApplyPriority(DevicePriority.Targets(tracker.Current, tracker.Current, preferences, request.Flow));
                        break;
                    case "ask": preferences.AskOnConnect = request.Value; SavePreferences(); if (!request.Value) tracker.Pending.RemoveAll(p => !p.IsDisconnection); break;
                    case "communications": preferences.IncludeCommunications = request.Value; SavePreferences(); break;
                    default: throw new InvalidOperationException("未知操作。");
                }
            }
            catch (Exception ex)
            {
                if (!preferencesSaved) preferences = beforePreferences;
                Program.Log(ex); error = (preferencesSaved ? "设置已保存，但本次应用未完成。" : "") + ex.Message;
                if (request.Action != "importSettings" && request.Action != "exportSettings" && request.Action != "darkMode") RefreshAudio(false);
            }
            // Detach the response on the owner thread before the pipe serializes it.
            return Wire.Decode<Reply>(Wire.Encode(new Reply { Error = error ?? audioError ?? priorityError, Warning = presetWarnings.Count == 0 ? null : String.Join("；", presetWarnings.Values), State = tracker.Current, Pending = tracker.Pending, DeviceSettings = settings,
                Preferences = preferences, BackupPath = backupPath, BackendPid = Process.GetCurrentProcess().Id, DolbyApplying = dolby.Applying,
                PromptPid = promptFrontend != null && !promptFrontend.HasExited ? promptFrontend.Id : 0,
                FrontendPid = frontend != null && !frontend.HasExited ? frontend.Id : 0 }));
        }
        private void SwitchTo(string id, bool quietAutomatic = false)
        {
            var device = tracker.Current.Devices.FirstOrDefault(d => d.Id == id);
            if (device == null) throw new InvalidOperationException("设备已断开，请选择其他设备。");
            var targets = new Dictionary<int, string> { { 0, id }, { 1, id } };
            if (preferences.IncludeCommunications) targets[2] = id;
            ApplyRoles(device.Flow, targets, quietAutomatic);
            priorityError = null;
        }
        private void RestorePrevious(Arrival arrival)
        {
            var targets = new Dictionary<int, string>();
            for (int role = 0; role < (preferences.IncludeCommunications ? 3 : 2); role++)
            {
                string id;
                if (arrival.PreviousDefaults.TryGetValue(arrival.Flow + ":" + role, out id))
                {
                    if (!tracker.Current.Devices.Any(d => d.Id == id)) throw new InvalidOperationException("旧设备已断开，无法恢复。请选择仍在线的设备。");
                    targets[role] = id;
                }
            }
            if (targets.Count == 0) throw new InvalidOperationException("没有可以恢复的旧设备。");
            ApplyRoles(arrival.Flow, targets);
        }
        private void ApplyRoles(int flow, Dictionary<int, string> targets, bool quietAutomatic = false)
        {
            var warnings = new List<string>();
            DeviceProfiles.Apply(flow, targets.Values, preferences.DeviceProfiles, audio,
                () => DeviceSelection.Apply(flow, targets, audio.Read, audio.SetRole), quietAutomatic ? (Action<string>)(message => warnings.Add(message)) : null);
            foreach (var id in targets.Values.Distinct())
            {
                presetWarnings.Remove(id);
                if (warnings.Count > 0)
                {
                    var device = tracker.Current.Devices.FirstOrDefault(d => d.Id == id);
                    presetWarnings[id] = (device == null ? "设备" : device.Name) + " 已切换；" + String.Join("；", warnings);
                }
            }
            priorityError = null;
            if (flow == 0) ObserveDolby(audio.Read(), true);
        }
        private DeviceSettingsInfo ReadDeviceSettings(string id)
        {
            var endpoint = audio.Read().Devices.FirstOrDefault(d => d.Id == id);
            if (endpoint == null) throw new InvalidOperationException("设备已断开，请重新连接后设置。");
            var info = new DeviceSettingsInfo { Device = endpoint, DeviceRule = DeviceAutomation.Rule(preferences, id) };
            try { info.CurrentVolume = (int)Math.Round(audio.ReadVolume(id) * 100); }
            catch (Exception ex) { info.VolumeError = "不能读取该设备音量：" + ex.Message; }
            if (endpoint.Flow == 0)
            {
                try { info.Spatial = audio.ReadSpatial(id); }
                catch (Exception ex) { info.SpatialError = ex.Message; }
            }
            DeviceProfile saved;
            info.Profile = preferences.DeviceProfiles.TryGetValue(id, out saved) ? Wire.Decode<DeviceProfile>(Wire.Encode(saved)) :
                new DeviceProfile { Volume = info.CurrentVolume,
                    SpatialFormat = info.Spatial != null && info.Spatial.Supported ? (DeviceProfiles.SameFormat(info.Spatial.CurrentFormat, "") ? "" : info.Spatial.CurrentFormat) : null };
            return info;
        }
        private void ShowFrontend(bool prompt)
        {
            if (prompt)
            {
                if (promptFrontend != null && !promptFrontend.HasExited) return;
                if (promptFrontend != null) promptFrontend.Dispose();
                promptFrontend = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--prompt --owner=" + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                return;
            }
            if (frontend != null && !frontend.HasExited)
            {
                if (!prompt) { ShowWindow(frontend.MainWindowHandle, 9); SetForegroundWindow(frontend.MainWindowHandle); }
                return;
            }
            if (frontend != null) frontend.Dispose();
            frontend = Process.Start(new ProcessStartInfo(Application.ExecutablePath, prompt ? "--prompt" : "--ui") { UseShellExecute = false });
        }
        private static Preferences LoadPreferences()
        {
            return PreferenceStore.Load(PreferenceStore.SettingsPath);
        }
        private void SavePreferences()
        {
            PreferenceStore.Save(PreferenceStore.SettingsPath, preferences);
        }
        protected override void ExitThreadCore()
        {
            exiting = true;
            dolby.Stop();
            debounce.Stop();
            if (frontend != null && !frontend.HasExited) frontend.CloseMainWindow();
            if (promptFrontend != null && !promptFrontend.HasExited) promptFrontend.CloseMainWindow();
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                exiting = true;
                if (dolby != null) dolby.Stop();
                if (server != null) server.Dispose();
                if (audio != null) audio.Dispose();
                if (debounce != null) debounce.Dispose();
                if (tray != null)
                {
                    tray.Visible = false;
                    if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                    if (tray.Icon != null) tray.Icon.Dispose();
                    tray.Dispose();
                }
                dispatcher.Dispose();
                if (frontend != null) frontend.Dispose();
                if (promptFrontend != null) promptFrontend.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
