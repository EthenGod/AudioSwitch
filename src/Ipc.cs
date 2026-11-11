// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Web.Script.Serialization;

namespace AudioSwitch
{
    internal static class Wire
    {
        internal const int MaxMessageBytes = 4 * 1024 * 1024;
        internal static readonly SecurityIdentifier UserSid = ReadUserSid();
        public static readonly string Identity = ReadIdentity();
        public static readonly string PipeName = "AudioSwitch-" + Identity;
        private static SecurityIdentifier ReadUserSid()
        {
            using (var identity = WindowsIdentity.GetCurrent()) return identity.User;
        }
        private static string ReadIdentity()
        {
            using (var process = Process.GetCurrentProcess()) return UserSid.Value + "-" + process.SessionId;
        }
        public static string Encode(object value) { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(value); }
        public static T Decode<T>(string text) { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<T>(text); }
        // Enforce the wire limit while reading, not after ReadLine has allocated
        // an arbitrarily large string. Require the complete newline-delimited frame.
        internal static async Task<string> ReadMessageAsync(Stream stream, int limit = MaxMessageBytes)
        {
            var buffer = new byte[4096];
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    int count = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, limit + 1 - (int)message.Length)).ConfigureAwait(false);
                    if (count == 0) throw new IOException("后台连接已中断，未收到完整消息。");
                    int end = Array.IndexOf(buffer, (byte)'\n', 0, count);
                    int length = end < 0 ? count : end + 1;
                    if (message.Length + length > limit) throw new InvalidDataException("消息过大，已停止读取。");
                    message.Write(buffer, 0, length);
                    if (end < 0) continue;
                    var bytes = message.GetBuffer();
                    int start = message.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                    int size = (int)message.Length - start - 1;
                    if (size > 0 && bytes[start + size - 1] == '\r') size--;
                    return new UTF8Encoding(false, true).GetString(bytes, start, size);
                }
            }
        }
        public static Reply Send(Request request)
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(1800);
                var writer = new StreamWriter(pipe) { AutoFlush = true };
                writer.WriteLine(Encode(request));
                var response = ReadMessageAsync(pipe);
                bool appliesSettings = request.Action == "switch" || request.Action == "new" || request.Action == "old" || request.Action == "alternative" || request.Action == "saveDeviceSettings" || request.Action == "saveBasicDeviceSettings" || request.Action == "priority" || request.Action == "deviceOrder" || request.Action == "refresh";
                if (!response.Wait(appliesSettings ? 20000 : 4000)) throw new TimeoutException("后台响应超时，请稍后重试。");
                return Decode<Reply>(response.Result);
            }
        }
    }
    internal sealed class PipeServer : IDisposable
    {
        private volatile bool stopped;
        private readonly object gate = new object();
        private NamedPipeServerStream active;
        // The handler freezes/serializes its reply on the state-owning thread.
        private readonly Func<Request, string> handler;
        private readonly Action<Request> afterReply;
        private readonly string pipeName;
        private readonly PipeSecurity security;
        public PipeServer(Func<Request, string> handler, Action<Request> afterReply = null, string pipeName = null)
        {
            this.handler = handler;
            this.afterReply = afterReply;
            this.pipeName = pipeName ?? Wire.PipeName;
            security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(Wire.UserSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            var thread = new Thread(Run) { IsBackground = true, Name = "AudioSwitch IPC" };
            thread.Start();
        }
        private void Run()
        {
            while (!stopped)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        lock (gate) { if (stopped) return; active = pipe; }
                        pipe.WaitForConnection();
                        var input = Wire.ReadMessageAsync(pipe);
                        if (!input.Wait(3000)) continue;
                        string response; Request request = null;
                        try { request = Wire.Decode<Request>(input.Result); response = handler(request); }
                        catch (Exception ex) { response = Wire.Encode(new Reply { Error = ex.Message }); }
                        try
                        {
                            var writer = new StreamWriter(pipe) { AutoFlush = true };
                            var output = writer.WriteLineAsync(response);
                            output.Wait(3000);
                        }
                        finally { if (request != null && afterReply != null) afterReply(request); }
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
