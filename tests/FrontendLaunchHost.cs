// Test-only process: production window launcher and pipe, synthetic devices, no audio service.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class FrontendLaunchHost
    {
        internal static int Run(string token, string directory)
        {
            Guid parsed; if (!Guid.TryParseExact(token,"N",out parsed)) return 2;
            bool created;
            using (var singleton = new Mutex(true,"Local\\AudioSwitch-Host-" + Wire.Identity,out created)) {
                if (!created) return 2;
                using (var stop = new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\AudioSwitch-LaunchTest-" + token))
                using (var dispatcher = new Control())
                using (var windows = new FrontendWindows(Path.Combine(directory,"AudioSwitch.exe")))
                using (var timer = new System.Windows.Forms.Timer { Interval = 200 }) {
                    var handle = dispatcher.Handle;
                    var device = new Endpoint { Id="test-output",Name="隔离验证设备（不会切换音频）",Flow=0 };
                    var state = new AudioState(); state.Devices.Add(device); state.Defaults["0:1"] = device.Id;
                    Func<Request,Reply> action = request => {
                        string error = null;
                        try {
                            if (request.Action == "show") windows.Show(false);
                            else if (request.Action == "testShowPrompt") windows.Show(true);
                            else if (request.Action != "snapshot") error = "隔离启动验证拒绝音频与配置写入。";
                        } catch (Exception ex) { error = ex.Message; }
                        return new Reply { PanelApiVersion=5,BackendPid=Process.GetCurrentProcess().Id,BackendExecutablePath=Path.Combine(directory,"AudioSwitch.exe"),FrontendPid=windows.PanelPid,PromptPid=windows.PromptPid,
                            State=state,Preferences=new Preferences(),Startup=new StartupState { Available=false },OperationError=error,
                            Warning="隔离启动验证：模拟设备，禁止音频及配置写入。",
                            Pending=new List<Arrival> { new Arrival { Token="launch-test",Flow=0,NewDevices=new List<Endpoint> {device},PreviousDefaults=new Dictionary<string,string>(),PreviousName="" } } };
                    };
                    using (var server = new PipeServer(r => (Reply)dispatcher.Invoke((Func<Reply>)(() => action(r))))) {
                        var lifetime = Stopwatch.StartNew(); timer.Tick += delegate { if (stop.WaitOne(0) || lifetime.Elapsed > TimeSpan.FromMinutes(10)) Application.ExitThread(); };
                        timer.Start(); Console.WriteLine("Launch fixture ready"); Application.Run(); timer.Stop();
                    }
                }
            }
            return 0;
        }
    }
}
