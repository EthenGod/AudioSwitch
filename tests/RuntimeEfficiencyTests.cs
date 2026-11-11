// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class RuntimeEfficiencyTests
    {
        private sealed class FragmentedStream : MemoryStream
        {
            internal FragmentedStream(byte[] bytes) : base(bytes) { }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            { return base.ReadAsync(buffer, offset, Math.Min(count, 1), token); }
        }

        internal static void Run(Action<bool, string> check)
        {
            string text = "{\"Error\":\"中文设备已断开\"}";
            using (var stream = new FragmentedStream(Encoding.UTF8.GetBytes("\ufeff" + text + "\r\n")))
                check(Wire.ReadMessageAsync(stream).GetAwaiter().GetResult() == text, "IPC preserves fragmented UTF-8, BOM and CRLF");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}\n")))
                check(Wire.ReadMessageAsync(stream, 3).GetAwaiter().GetResult() == "{}", "IPC accepts exact frame limit including delimiter");
            foreach (var bytes in new[] { Encoding.UTF8.GetBytes("{}"), new byte[] { 0xff, 10 }, new byte[] { 13 } })
            {
                bool rejected = false;
                using (var stream = new MemoryStream(bytes))
                    try { Wire.ReadMessageAsync(stream).GetAwaiter().GetResult(); }
                    catch (IOException) { rejected = true; }
                    catch (DecoderFallbackException) { rejected = true; }
                check(rejected, "IPC rejects truncated or invalid UTF-8 messages");
            }
            using (var stream = new MemoryStream(Enumerable.Repeat((byte)'x', 100000).ToArray()))
            {
                bool rejected = false;
                try { Wire.ReadMessageAsync(stream, 128).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && stream.Position == 129, "oversized IPC input stops at limit plus one without consuming the rest");
            }
            FrozenReply(check);
            EventBurst(check);
        }

        private static void FrozenReply(Action<bool, string> check)
        {
            string pipeName = "AudioSwitch-test-" + Guid.NewGuid().ToString("N");
            var preferences = new Preferences();
            preferences.DeviceAliases["fixture"] = "保存的名称";
            using (var server = new PipeServer(request => {
                if (request.Action == "fail") throw new InvalidOperationException("测试错误");
                string frozen = Wire.Encode(new Reply { Preferences = preferences });
                preferences.DeviceAliases["fixture"] = "后续变化";
                return frozen;
            }, pipeName: pipeName))
            {
                var reply = Exchange(pipeName, "snapshot");
                check(reply.Preferences.DeviceAliases["fixture"] == "保存的名称", "serialized reply cannot observe later owner-thread mutations");
                check(Exchange(pipeName, "fail").Error == "测试错误", "serialized IPC handler retains structured errors");
                check(Exchange(pipeName, "snapshot").Preferences.DeviceAliases["fixture"] == "后续变化", "server recovers after a failed request");
            }
        }

        private static Reply Exchange(string name, string action)
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(3000);
                var writer = new StreamWriter(pipe) { AutoFlush = true };
                writer.WriteLine(Wire.Encode(new Request { Action = action }));
                var read = Wire.ReadMessageAsync(pipe);
                if (!read.Wait(3000)) throw new TimeoutException("Isolated IPC test timed out");
                return Wire.Decode<Reply>(read.Result);
            }
        }

        private static void EventBurst(Action<bool, string> check)
        {
            var posted = new ConcurrentQueue<Action>();
            int signals = 0, refreshes = 0;
            using (var batch = new DeviceEventBatch(() => refreshes++))
            {
                Parallel.For(0, 10000, i => batch.Post(posted.Enqueue, () => signals++));
                check(posted.Count == 1, "10000 concurrent device notifications queue only one owner-thread wake-up");
                Action callback; posted.TryDequeue(out callback); callback();
                Pump(350);
                check(signals == 1 && refreshes == 1, "coalesced notifications still refresh once");
                batch.Post(posted.Enqueue, () => signals++);
                check(posted.Count == 1, "a later device change can queue a fresh wake-up");
                batch.Stop(); posted.TryDequeue(out callback); callback(); Pump(200);
                check(signals == 1 && refreshes == 1, "queued callback after stop cannot restart a refresh");
            }
            using (var batch = new DeviceEventBatch(() => { }))
            {
                try { batch.Post(action => { throw new InvalidOperationException(); }, () => { }); }
                catch (InvalidOperationException) { }
                batch.Post(posted.Enqueue, () => { });
                check(posted.Count == 1, "failed dispatch releases the pending wake-up slot");
                batch.Dispose(); Action callback; posted.TryDequeue(out callback); callback();
            }
        }

        private static void Pump(int duration)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < duration) { Application.DoEvents(); Thread.Sleep(5); }
        }

        // Isolated synthetic payload plus read-only Core Audio calls. No backend,
        // configuration writes, device switching or Dolby worker is started.
        internal static void Benchmark()
        {
            var reply = new Reply { State = new AudioState(), Preferences = new Preferences(), Pending = new List<Arrival>() };
            for (int i = 0; i < 50; i++)
            {
                string id = "fixture-" + i;
                var device = new Endpoint { Id = id, Name = "测试设备 " + i, Flow = i % 2 };
                reply.State.Devices.Add(device); reply.Preferences.DeviceOrder.Add(device);
                reply.Preferences.DeviceProfiles[id] = new DeviceProfile { Volume = 50, Dolby = new DolbyProfile() };
            }
            Func<string> before = () => {
                Wire.Decode<Preferences>(Wire.Encode(reply.Preferences));
                return Wire.Encode(Wire.Decode<Reply>(Wire.Encode(reply)));
            };
            Func<string> after = () => Wire.Encode(reply);
            if (before() != after()) throw new Exception("Snapshot payload changed");
            Measure("snapshot before", 500, () => { before(); });
            Measure("snapshot after", 500, () => { after(); });
            using (var audio = new AudioService())
            {
                Measure("default guard before", 100, () => { audio.Read().Default(0, 1); });
                Measure("default guard after", 100, () => { audio.ReadDefault(0, 1); });
            }
        }

        private static void Measure(string label, int count, Action action)
        {
            for (int i = 0; i < 5; i++) action();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int collections = GC.CollectionCount(0);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) action();
            Console.WriteLine(label + ": " + count + " calls, " + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms, Gen0=" + (GC.CollectionCount(0) - collections));
        }
    }
}
