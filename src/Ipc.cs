using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AudioSwitch
{
    internal static class Wire
    {
        public static readonly string Identity = WindowsIdentity.GetCurrent().User.Value + "-" + Process.GetCurrentProcess().SessionId;
        public static readonly string PipeName = "AudioSwitch-" + Identity;
        public static string Encode(object value) { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(value); }
        public static T Decode<T>(string text) { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<T>(text); }
        public static Reply Send(Request request)
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(1800);
                var writer = new StreamWriter(pipe) { AutoFlush = true };
                var reader = new StreamReader(pipe);
                writer.WriteLine(Encode(request));
                var response = reader.ReadLineAsync();
                bool appliesSettings = request.Action == "switch" || request.Action == "new" || request.Action == "old" || request.Action == "alternative" || request.Action == "saveDeviceSettings" || request.Action == "priority" || request.Action == "deviceOrder" || request.Action == "refresh";
                if (!response.Wait(appliesSettings ? 20000 : 4000)) throw new TimeoutException("后台响应超时，请稍后重试。");
                if (response.Result == null) throw new IOException("后台已退出。");
                return Decode<Reply>(response.Result);
            }
        }
    }
    internal sealed class PipeServer : IDisposable
    {
        private volatile bool stopped;
        private readonly object gate = new object();
        private NamedPipeServerStream active;
        private readonly Func<Request, Reply> handler;
        public PipeServer(Func<Request, Reply> handler)
        {
            this.handler = handler;
            var thread = new Thread(Run) { IsBackground = true, Name = "AudioSwitch IPC" };
            thread.Start();
        }
        private void Run()
        {
            while (!stopped)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(Wire.PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        lock (gate) { if (stopped) return; active = pipe; }
                        pipe.WaitForConnection();
                        var reader = new StreamReader(pipe);
                        var input = reader.ReadLineAsync();
                        if (!input.Wait(3000) || input.Result == null || input.Result.Length > 4 * 1024 * 1024) continue;
                        Reply response;
                        try { response = handler(Wire.Decode<Request>(input.Result)); }
                        catch (Exception ex) { response = new Reply { Error = ex.Message }; }
                        var writer = new StreamWriter(pipe) { AutoFlush = true };
                        var output = writer.WriteLineAsync(Wire.Encode(response));
                        output.Wait(3000);
                    }
                }
                catch (Exception ex)
                {
                    if (!stopped) { Program.Log(ex); Thread.Sleep(150); }
                }
                finally { lock (gate) { active = null; } }
            }
        }
        public void Dispose()
        {
            stopped = true;
            lock (gate) { if (active != null) active.Dispose(); }
        }
    }
}
