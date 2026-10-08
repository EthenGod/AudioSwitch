// Test-only bridge for real reads. Never constructs TrayHost or a Dolby write queue.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class PanelReadOnlyHost
    {
        internal static int Run(string token, bool waitWorker = false)
        {
            Guid parsed;
            if (!Guid.TryParseExact(token, "N", out parsed)) return 2;
            bool created;
            using (var singleton = new Mutex(true, "Local\\AudioSwitch-Host-" + Wire.Identity, out created))
            {
                if (!created) { Console.Error.WriteLine("已有声间后台，不启动联调入口。"); return 2; }
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\AudioSwitch-ReadOnlyTest-" + token))
                using (var dispatcher = new Control())
                using (var audio = new AudioService())
                using (var timer = new System.Windows.Forms.Timer { Interval = 200 })
                {
                    var handle = dispatcher.Handle;
                    string beforeHash = ConfigurationHash();
                    var before = Capture(audio); var actions = new List<string>();
                    Func<Request, Reply> read = request => {
                        actions.Add(request.Action);
                        var prefs = ReadPreferences(); var state = audio.Read();
                        var reply = new Reply {
                            PanelApiVersion = 5, State = state, Preferences = prefs, Pending = new List<Arrival>(),
                            BackendExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, waitWorker ? "readonly-wait-fixture/AudioSwitch.exe" : "AudioSwitch.exe"),
                            BackendPid = Process.GetCurrentProcess().Id,
                            Startup = new StartupState { Available = false, Enabled = false, Message = "只读联调不读取或修改自启登记。" },
                            Warning = waitWorker ? "只读等待夹具：辅助进程只等待取消，不读取或写入音频。所有保存与切换均被拒绝。" : "只读联调入口：设备和读取结果来自本机；所有保存、切换和应用请求均被拒绝。未运行托盘自动规则。"
                        };
                        if (request.Action == "deviceSettings") {
                            var device = state.Devices.Single(d => d.Id == request.DeviceId);
                            DeviceProfile profile; prefs.DeviceProfiles.TryGetValue(device.Id, out profile);
                            var settings = new DeviceSettingsInfo { Device = device, Profile = profile, DeviceRule = DeviceAutomation.Rule(prefs, device.Id) };
                            try { settings.CurrentVolume = (int)Math.Round(audio.ReadVolume(device.Id) * 100); } catch (Exception ex) { settings.VolumeError = ex.Message; }
                            if (device.Flow == 0) { try { settings.Spatial = audio.ReadSpatial(device.Id); } catch (Exception ex) { settings.SpatialError = ex.Message; } }
                            reply.DeviceSettings = settings;
                        }
                        else if (request.Action == "exportSettings") reply.ConfigurationJson = PreferenceStore.Export(prefs);
                        else if (request.Action != "snapshot") reply.OperationError = "只读联调禁止保存、设备切换、音效应用及其他写入。";
                        return reply;
                    };
                    using (var server = new PipeServer(request => (Reply)dispatcher.Invoke((Func<Reply>)(() => read(request)))))
                    {
                        var lifetime = Stopwatch.StartNew();
                        timer.Tick += delegate { if (stop.WaitOne(0) || lifetime.Elapsed > TimeSpan.FromMinutes(10)) Application.ExitThread(); };
                        timer.Start(); Console.WriteLine("Readonly bridge ready: " + Process.GetCurrentProcess().Id);
                        Application.Run(); timer.Stop();
                    }
                    var after = Capture(audio); string afterHash = ConfigurationHash();
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "readonly-session-" + token + ".json");
                    File.WriteAllText(path, Wire.Encode(new { ConfigurationUnchanged = beforeHash == afterHash, Before = before, After = after, Actions = actions }), new System.Text.UTF8Encoding(false));
                    Console.WriteLine("Readonly audit: " + path);
                    return beforeHash == afterHash ? 0 : 3;
                }
            }
        }
        private static Preferences ReadPreferences()
        {
            // Parse rather than Load: legacy configuration must not be migrated during reads.
            return File.Exists(PreferenceStore.SettingsPath) ? PreferenceStore.Parse(PreferenceStore.ReadFile(PreferenceStore.SettingsPath)) : new Preferences();
        }
        private static string ConfigurationHash()
        {
            if (!File.Exists(PreferenceStore.SettingsPath)) return null;
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(PreferenceStore.SettingsPath)));
        }
        private static object Capture(AudioService audio)
        {
            var state = audio.Read(); var volumes = new Dictionary<string, double>(); var spatial = new Dictionary<string, string>(); var errors = new List<string>();
            foreach (var device in state.Devices) {
                try { volumes[device.Id] = audio.ReadVolume(device.Id); } catch (Exception ex) { errors.Add(device.Id + ": " + ex.Message); }
                if (device.Flow == 0) { try { spatial[device.Id] = audio.ReadSpatial(device.Id).CurrentFormat; } catch (Exception ex) { errors.Add(device.Id + ": " + ex.Message); } }
            }
            return new { State = state, Volumes = volumes, Spatial = spatial, Errors = errors };
        }
    }
}
