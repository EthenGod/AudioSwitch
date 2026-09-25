using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class Tests
    {
        private static int passed;
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Contains("--settings-layout")) { TestSettingsLayout(); return 0; }
                if (args.Contains("--lifecycle") || args.Contains("--dolby-smoke"))
                {
                    Mutex existing;
                    if (Mutex.TryOpenExisting("Local\\AudioSwitch-Host-" + Wire.Identity, out existing))
                    { existing.Dispose(); throw new InvalidOperationException("请先退出当前声间，再运行生命周期或 Dolby 实机测试；不会启动第二个后台。"); }
                }
                RunStateTests();
                RunDisconnectionTests();
                RunSelectionTests();
                RunProfileTests();
                RunPriorityTests();
                RunWhitelistTests();
                RunWhitelistRegressionTests();
                RunConfigurationTests();
                RunDolbyTests();
                using (var audio = new AudioService())
                {
                    audio.Listen(delegate { });
                    var actual = audio.Read();
                    Check(actual.Devices.All(d => !String.IsNullOrEmpty(d.Id) && !String.IsNullOrEmpty(d.Name)), "live Core Audio enumeration + notification registration");
                    Console.WriteLine("Live endpoints: " + actual.Devices.Count);
                    foreach (var d in actual.Devices) Console.WriteLine((d.Flow == 0 ? "OUT " : "IN  ") + d.Name);
                    foreach (var d in actual.Devices)
                    {
                        float volume = audio.ReadVolume(d.Id);
                        Check(volume >= 0 && volume <= 1, "live endpoint volume read: " + d.Name);
                        if (d.Flow == 0)
                        {
                            var spatial = audio.ReadSpatial(d.Id);
                            Check(spatial.Options.Count >= 1 && spatial.CurrentFormat != null, "live spatial settings read: " + d.Name);
                            Console.WriteLine("Spatial available: " + String.Join(", ", spatial.Options.Select(o => o.Name)));
                        }
                    }
                }
                if (args.Contains("--render")) { Render(); TestDashboardWorkspace(); TestDevicePrompt(); TestEventBatch(); TestSettingsDialog(); TestSettingsLayout(); TestPriorityDialog(); TestDismissibleNotice(); TestDolbyEditor(); TestThemeAndControls(); TestNoticesAndMenus(); }
                if (args.Contains("--settings-smoke")) SettingsSmoke();
                if (args.Contains("--lifecycle")) Lifecycle();
                if (args.Contains("--dolby-smoke")) DolbySmoke();
                if (args.Contains("--worker-cancel")) TestWorkerCancellationSignal();
                Console.WriteLine("PASS: " + passed + " checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            passed++; Console.WriteLine("OK " + label);
        }
        private sealed class FakeDolby : IDolbyAccess
        {
            internal Dictionary<int, int> Values = new Dictionary<int, int> { { 7, 4 }, { 38, 4 }, { 5, -1 }, { 9, 1 }, { 11, 1 }, { 13, 0 }, { 73, 0 }, { 42, 2 } };
            internal Dictionary<int, double> Strengths = new Dictionary<int, double> { { 45, .5 }, { 47, .625 }, { 49, .2 } };
            internal int[] Eq = Enumerable.Range(0, 20).ToArray();
            internal List<int> Writes = new List<int>(); internal int Fail; internal bool Quantize;
            internal Action<int> AfterRead;
            internal Func<int, double> StrengthResult;
            public int Read(int op) { int result = Values[op]; if (AfterRead != null) AfterRead(op); return result; }
            public double ReadStrength(int op) { double result = StrengthResult == null ? Strengths[op] : StrengthResult(op); if (AfterRead != null) AfterRead(op); return result; }
            public int[] ReadEq() { var result = (int[])Eq.Clone(); if (AfterRead != null) AfterRead(17); return result; }
            public void Write(int op, int value) { Writes.Add(op); Values[op - 1] = value; if (Fail == op) { Fail = 0; throw new Exception("injected partial write"); } }
            public void WriteStrength(int op, double value) { Writes.Add(op); Strengths[op - 1] = Quantize ? Math.Round(value * 5) / 5 : value; }
            public void WriteEq(int[] value) { Writes.Add(18); Eq = (int[])value.Clone(); if (Fail == 18) { Fail = 0; throw new Exception("injected EQ failure"); } }
        }
        private static void RunDolbyTests()
        {
            RunEqualizerTests();
            RunDolbyReviewTests();
            var profile = new DolbyProfile { MainProfile = 4, SubProfile = 5, Eq = Enumerable.Range(-10, 20).ToArray(), Enabled = true, Surround = false, Dialog = false, Leveler = true, AutoSwitch = false, SurroundStrength = .375, DialogStrength = .5, LevelerStrength = .2 };
            var prefs = new Preferences(); prefs.DeviceProfiles["output"] = new DeviceProfile { Dolby = profile };
            var roundtrip = PreferenceStore.Parse(PreferenceStore.Export(prefs)).DeviceProfiles["output"].Dolby;
            Check(Wire.Encode(profile) == Wire.Encode(roundtrip), "Dolby all fields and twenty EQ bands survive unified backup roundtrip");
            foreach (string invalid in new[] { "{\"Bogus\":1}", "{\"MainProfile\":2}", "{\"Enabled\":1}", "{\"Eq\":[0]}", "{\"MainProfile\":4}", "{\"SubProfile\":5}", "{\"Ieq\":2}", "{\"SurroundStrength\":1.01}", "{\"SurroundStrength\":\"0.5\"}" })
            { bool rejected = false; try { PreferenceStore.Parse("{\"DeviceProfiles\":{\"out\":{\"Dolby\":" + invalid + "}}}"); } catch { rejected = true; } Check(rejected, "invalid Dolby backup rejected: " + invalid); }
            bool mic = false; try { DeviceProfiles.Validate(new DeviceProfile { Dolby = profile }, 1); } catch { mic = true; } Check(mic, "microphone cannot contain Dolby playback profile");
            Check(DolbyProfiles.EndpointGuid("{0.0.0.00000000}.{8c102a30-8726-4bf4-a1be-d9724570732e}") == "{8C102A30-8726-4BF4-A1BE-D9724570732E}", "DAX endpoint identity uses exact final GUID");
            bool wrongId = false; try { DolbyProfiles.EndpointGuid("{0.0.1.00000000}.{8c102a30-8726-4bf4-a1be-d9724570732e}"); } catch { wrongId = true; } Check(wrongId, "input identity cannot pass Dolby output binding");
            var fake = new FakeDolby(); var original = DolbyProfiles.Capture(fake);
            DolbyProfiles.Apply(profile, fake, delegate { });
            Check(fake.Read(38) == 5 && fake.Read(9) == 0 && fake.Read(13) == 1 && fake.Eq.SequenceEqual(profile.Eq), "Dolby preset slot, toggles, strengths and EQ applied together");
            int writes = fake.Writes.Count; DolbyProfiles.Apply(profile, fake, delegate { });
            Check(fake.Writes.Count == writes, "same Dolby values cause no redundant native writes");
            fake = new FakeDolby { Fail = 18 }; bool failed = false;
            try { DolbyProfiles.Apply(profile, fake, delegate { }); } catch { failed = true; }
            var recoveredEq = DolbyProfiles.Capture(fake);
            // JSON property ordering can vary after reflection caches are populated.
            // Compare recovered values, including every EQ band, rather than serialized order.
            Check(failed && typeof(DolbyProfile).GetProperties().All(property => {
                object expected = property.GetValue(original, null), actual = property.GetValue(recoveredEq, null);
                return expected is int[] ? actual is int[] && ((int[])expected).SequenceEqual((int[])actual) : Object.Equals(expected, actual);
            }), "partially written EQ failure restores preset, toggles, strengths and original EQ");
            fake = new FakeDolby { Fail = 12 }; failed = false;
            try { DolbyProfiles.Apply(new DolbyProfile { MainProfile = 0, Dialog = false }, fake, delegate { }); } catch { failed = true; }
            Check(failed && fake.Read(7) == 4 && fake.Read(38) == 4 && fake.Read(11) == 1, "main-profile change and failed effect restore original main/sub selection");
            fake = new FakeDolby { Quantize = true }; string warning = DolbyProfiles.Apply(new DolbyProfile { LevelerStrength = .075 }, fake, delegate { });
            Check(warning != null && warning.Contains("0%"), "driver strength quantization returns actual percentage as warning");
            fake = new FakeDolby(); failed = false;
            try { DolbyProfiles.Apply(profile, fake, () => { throw new Exception("output changed"); }); } catch { failed = true; }
            Check(failed && fake.Writes.Count == 0, "default output guard prevents stale Dolby writes");
            TestDolbyQueue();
        }
        private static void RunDolbyReviewTests()
        {
            foreach (int op in new[] { 9, 45, 17, 38 })
            {
                var fake = new FakeDolby(); bool cancelled = false, rejected = false;
                fake.AfterRead = read => { if (read == op) cancelled = true; };
                var p = op == 9 ? new DolbyProfile { Surround = false } : op == 45 ? new DolbyProfile { SurroundStrength = .75 } :
                    op == 17 ? new DolbyProfile { MainProfile = 4, SubProfile = 4, Eq = new int[20] } : new DolbyProfile { MainProfile = 0 };
                try { DolbyProfiles.Apply(p, fake, () => { if (cancelled) throw new OperationCanceledException(); }, delegate { }); } catch { rejected = true; }
                Check(rejected && fake.Writes.Count == 0, "cancellation during native read prevents subsequent write: " + op);
            }
            foreach (int changed in new[] { 7, 38 })
            {
                var fake = new FakeDolby(); bool rejected = false;
                fake.AfterRead = read => { if (read == 45) fake.Values[changed] = changed == 7 ? 0 : 5; };
                try { DolbyProfiles.Capture(fake); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "capture rejects a preset or custom slot changed midway: " + changed);
            }
            foreach (double value in new[] { Double.NaN, Double.PositiveInfinity, -.1, 1.1 })
            {
                var fake = new FakeDolby(); fake.Strengths[45] = value; bool rejected = false;
                try { DolbyProfiles.Apply(new DolbyProfile { SurroundStrength = .75 }, fake, delegate { }); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && fake.Writes.Count == 0, "invalid original strength cannot become an unusable rollback value: " + value);
            }
            foreach (var invalid in new[] { new int[19], Enumerable.Repeat(193, 20).ToArray() })
            {
                var fake = new FakeDolby { Eq = invalid }; bool rejected = false;
                try { DolbyProfiles.Apply(new DolbyProfile { MainProfile = 4, SubProfile = 4, Eq = new int[20] }, fake, delegate { }); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && fake.Writes.Count == 0, "invalid original EQ is rejected before overwriting it");
            }
            var rollback = new FakeDolby(); string message = null;
            rollback.StrengthResult = op => op == 45 && rollback.Writes.Count(n => n == 46) > 1 ? Double.NaN : rollback.Strengths[op];
            rollback.AfterRead = op => { if (op == 47) throw new InvalidOperationException("read failed"); };
            try { DolbyProfiles.Apply(new DolbyProfile { SurroundStrength = .75, DialogStrength = .8 }, rollback, delegate { }); } catch (InvalidOperationException ex) { message = ex.Message; }
            Check(message != null && message.Contains("未能恢复"), "NaN during rollback verification cannot report restoration success");
        }
        private static void RunEqualizerTests()
        {
            // Read from this machine's Access 3.27.12250.0, not generated by the converter.
            int[] captured = { -27, 16, 26, 56, 77, 60, 43, 26, 25, 24, 23, 40, 56, 77, 51, 21, -22, -31, -42, -66 };
            int[] anchors = { -27, 16, 26, 56, 77, 26, 23, 77, -22, -42 };
            decimal[] db = anchors.Select(n => n / 16m).ToArray();
            Check(DolbyEqualizer.ToTen(captured).SequenceEqual(db), "Access ten-point projection selects measured DAP anchors");
            Check(DolbyEqualizer.ToTwenty(db).SequenceEqual(captured), "ten-point conversion matches real Access twenty-band curve including extrapolated tail");
            db[0] = 173 / 16m; db[1] = 189 / 16m; db[9] = 12m;
            int[] changed = { 173, 189, 26, 56, 77, 60, 43, 26, 25, 24, 23, 40, 56, 77, 51, 21, -22, 77, 192, 192 };
            Check(DolbyEqualizer.ToTwenty(db).SequenceEqual(changed), "conversion matches independent Access low/high control changes and saturation");
            foreach (decimal gain in new[] { -12m, 0m, 12m })
                Check(DolbyEqualizer.ToTwenty(Enumerable.Repeat(gain, 10).ToArray()).All(n => n == gain * 16), "flat dB curve preserves every band: " + gain);
            Check(DolbyEqualizer.ToTwenty(Enumerable.Repeat(.1m, 10).ToArray()).All(n => n == 2), "fractional dB rounds to supported driver resolution");
            foreach (var invalid in new[] { (decimal[])null, new decimal[9], Enumerable.Repeat(12.01m, 10).ToArray() })
            { bool rejected = false; try { DolbyEqualizer.ToTwenty(invalid); } catch (ArgumentException) { rejected = true; } Check(rejected, "invalid ten-point input rejected"); }
            foreach (var invalid in new[] { (int[])null, new int[19], Enumerable.Repeat(-193, 20).ToArray() })
            { bool rejected = false; try { DolbyEqualizer.ToTen(invalid); } catch (ArgumentException) { rejected = true; } Check(rejected, "invalid twenty-band input rejected"); }
            var random = new Random(42);
            bool bounded = true, stable = true;
            for (int i = 0; i < 1000; i++)
            {
                var input = Enumerable.Range(0, 10).Select(n => random.Next(-192, 193) / 16m).ToArray();
                var result = DolbyEqualizer.ToTwenty(input);
                bounded &= result.All(n => n >= -192 && n <= 192);
                stable &= DolbyEqualizer.ToTen(result).SequenceEqual(input);
            }
            Check(bounded && stable, "1000 random curves remain bounded and preserve all ten anchor values");
        }
        private static void TestDolbyQueue()
        {
            var callbacks = new System.Collections.Concurrent.ConcurrentQueue<Action>(); var started = new ManualResetEvent(false); var release = new ManualResetEvent(false);
            var jobs = new List<string>(); var completed = new List<string>();
            CancellationToken firstToken = CancellationToken.None;
            var queue = new DolbyQueue(a => callbacks.Enqueue(a), (id, p, token) => { lock (jobs) jobs.Add(id + ":" + p.SubProfile); if (id == "a") firstToken = token; started.Set(); release.WaitOne(2500); return new DolbyResult(); }, (id, r) => completed.Add(id));
            var profile = new DolbyProfile { MainProfile = 4, SubProfile = 4 };
            queue.Observe("a", profile); Check(started.WaitOne(1500), "Dolby worker starts asynchronously");
            queue.Observe("a", profile); queue.Observe("b", profile); queue.Observe("c", profile); profile.SubProfile = 5;
            Check(firstToken.IsCancellationRequested, "superseded Dolby job receives cancellation while running");
            release.Set(); var watch = Stopwatch.StartNew();
            while (completed.Count == 0 && watch.ElapsedMilliseconds < 3000) { Action callback; while (callbacks.TryDequeue(out callback)) callback(); Thread.Sleep(5); }
            Check(jobs.SequenceEqual(new[] { "a:4", "c:4" }) && completed.SequenceEqual(new[] { "c" }), "rapid switching serializes work, drops superseded results and snapshots latest profile");
            queue.Observe("d", null); queue.Observe("d", profile); Check(jobs.Count == 2, "unconfigured output and duplicate device events do not launch workers");
            queue.Stop(); queue.Observe("e", profile, true); Check(jobs.Count == 2, "stopped backend schedules no Dolby work");
            started.Dispose(); release.Dispose();
            TestDolbyCancellation();
        }
        private static void TestDolbyCancellation()
        {
            foreach (bool stop in new[] { false, true })
            {
                var callbacks = new System.Collections.Concurrent.ConcurrentQueue<Action>();
                using (var started = new ManualResetEvent(false))
                using (var ended = new ManualResetEvent(false))
                {
                    int writes = 0, results = 0;
                    var queue = new DolbyQueue(a => callbacks.Enqueue(a), (id, p, token) => {
                        started.Set(); if (!token.WaitHandle.WaitOne(2000)) Interlocked.Increment(ref writes);
                        ended.Set(); return new DolbyResult();
                    }, (id, r) => results++);
                    queue.Observe("a", new DolbyProfile { Enabled = true });
                    Check(started.WaitOne(1500), "cancellable Dolby job entered worker");
                    if (stop) queue.Stop(); else queue.Cancel();
                    Check(ended.WaitOne(1500) && writes == 0, stop ? "backend exit cancels active Dolby writes" : "save/import cancellation stops active Dolby writes");
                    var watch = Stopwatch.StartNew();
                    while (queue.Applying && watch.ElapsedMilliseconds < 2000) { Action action; while (callbacks.TryDequeue(out action)) action(); Thread.Sleep(2); }
                    Check(results == 0, "cancelled Dolby completion cannot revive panel warning"); queue.Stop();
                }
            }
            var fake = new FakeDolby(); string original = Wire.Encode(DolbyProfiles.Capture(fake)); bool failed = false;
            try { DolbyProfiles.Apply(new DolbyProfile { Surround = false, Dialog = false }, fake,
                () => { if (fake.Writes.Count > 0) throw new OperationCanceledException(); }, delegate { }); } catch { failed = true; }
            Check(failed && fake.Writes.SequenceEqual(new[] { 10, 10 }) && Wire.Encode(DolbyProfiles.Capture(fake)) == original, "cancellation restores preceding writes when original output is still current");
            fake = new FakeDolby(); fake.Values[73] = -1;
            DolbyProfiles.Apply(new DolbyProfile { AutoSwitch = false, MainProfile = 0 }, fake, delegate { });
            Check(fake.Writes.First() == 74, "Dolby content switching disabled before preset selection");
            fake = new FakeDolby();
            DolbyProfiles.Apply(new DolbyProfile { AutoSwitch = true, Surround = false }, fake, delegate { });
            Check(fake.Writes.Last() == 74, "Dolby content switching enabled after effect writes");
        }
        private static void TestDolbyEditor()
        {
            var profile = DolbyProfiles.Capture(new FakeDolby());
            var info = new DeviceSettingsInfo { Device = Device("Realtek 耳机", 0), CurrentVolume = 35, Profile = new DeviceProfile { Dolby = profile } };
            Request saved = null;
            using (var dialog = new DeviceSettingsDialog(info, r => { saved = r; return Task.FromResult(new Reply()); }))
            {
                dialog.Show(); var tabs = Descendants(dialog).OfType<SectionTabs>().Single(); tabs.SelectedIndex = 1; Application.DoEvents();
                string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "dolby-settings.png"));
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, dialog.ClientRectangleWithFrame()); bitmap.Save(path); }
                var editor = Descendants(dialog).OfType<DolbyEditor>().Single(); editor.AutoScrollPosition = new Point(0, 600); Application.DoEvents();
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, dialog.ClientRectangleWithFrame()); bitmap.Save(path.Replace(".png", "-eq.png")); }
                var mode = Descendants(editor).OfType<ComboBox>().Single(c => c.Name == "dolbyEqView");
                mode.SelectedIndex = 1; Application.DoEvents();
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, dialog.ClientRectangleWithFrame()); bitmap.Save(path.Replace(".png", "-eq-raw.png")); }
                mode.SelectedIndex = 0; mode.SelectedIndex = 1; mode.SelectedIndex = 0;
                Check(editor.Value().Eq.SequenceEqual(profile.Eq), "switching between ten-point and twenty-band views never resamples a saved curve");
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "save").PerformClick();
                Check(saved != null && Wire.Encode(saved.Profile.Dolby) == Wire.Encode(profile), "Dolby editor preserves captured values and EQ in save-only request");
            }
            using (var dialog = new DeviceSettingsDialog(info, r => { saved = r; return Task.FromResult(new Reply()); }))
            {
                dialog.Show(); Descendants(dialog).OfType<CheckBox>().Single(c => c.Name == "useDolby").Checked = false;
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "save").PerformClick();
                Check(saved.Profile.Dolby == null, "Dolby auto-application can be disabled independently per device");
            }
            using (var editor = new DolbyEditor("test", profile))
            {
                var fields = Descendants(editor).OfType<NumberBox>().ToList();
                fields.Single(n => n.Name == "dolbyDb4").Value = 6m;
                var points = DolbyEqualizer.ToTen(profile.Eq); points[4] = 6m;
                Check(editor.Value().Eq.SequenceEqual(DolbyEqualizer.ToTwenty(points)), "editing dB writes converted twenty-band values into the saved device profile");
                fields.Single(n => n.Name == "dolbyBand5").Value = -123;
                Check(editor.Value().Eq[5] == -123, "advanced editing preserves arbitrary non-anchor raw values");
                fields.Single(n => n.Name == "dolbyBand0").Value = -192;
                Check(fields.Single(n => n.Name == "dolbyDb0").Value == -12m, "raw anchor editing refreshes displayed dB without regenerating intermediate bands");
                Check(editor.Value().Eq[5] == -123, "refreshing dB projection leaves custom intermediate values intact");
                var prefs = new Preferences(); prefs.DeviceProfiles["out"] = new DeviceProfile { Dolby = editor.Value() };
                Check(PreferenceStore.Parse(PreferenceStore.Export(prefs)).DeviceProfiles["out"].Dolby.Eq.SequenceEqual(editor.Value().Eq), "converted and advanced EQ share the existing lossless backup field");
                Descendants(editor).OfType<CheckBox>().Single(c => c.Name == "dolbyUseEq").Checked = false;
                Check(editor.Value().Eq == null, "disabled equalizer keeps its do-not-change semantics");
            }
            using (var editor = new DolbyEditor("test", profile))
            {
                var number = Descendants(editor).OfType<NumberBox>().Single(n => n.Name == "dolbyDb0");
                number.Text = "6.00";
                Check(editor.Value().Eq[0] == 96, "saving commits typed dB text even before the input loses focus");
            }
            using (var editor = new DolbyEditor("test", profile))
            {
                var sliders = Descendants(editor).OfType<LevelSlider>().ToArray();
                Check(sliders.Length == 10 && editor.Value().Eq.SequenceEqual(profile.Eq), "creating ten sliders preserves every original raw EQ band");
                var slider = sliders.Single(s => s.Name == "dolbySlider4");
                slider.Value = 630;
                var points = DolbyEqualizer.ToTen(profile.Eq); points[4] = 6.3m;
                Check(editor.Value().Eq.SequenceEqual(DolbyEqualizer.ToTwenty(points)), "EQ slider edits use the calibrated ten-to-twenty conversion");
                var fields = Descendants(editor).OfType<NumberBox>().ToArray();
                Check(fields.Single(n => n.Name == "dolbyDb4").Value == 6.3m, "slider movement updates the exact dB input");
                fields.Single(n => n.Name == "dolbyDb4").Value = -7.25m;
                Check(slider.Value == -725, "precise typed dB input moves the slider without rounding to drag steps");
                fields.Single(n => n.Name == "dolbyBand5").Value = 137;
                var before = editor.Value().Eq;
                var mode = Descendants(editor).OfType<ChoiceBox>().Single(c => c.Name == "dolbyEqView");
                mode.SelectedIndex = 1; mode.SelectedIndex = 0;
                Check(editor.Value().Eq.SequenceEqual(before), "changing EQ views does not regenerate custom intermediate bands");
                var key = typeof(LevelSlider).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                key.Invoke(slider, new object[] { new KeyEventArgs(Keys.End) });
                Check(slider.Value == 1200 && fields.Single(n => n.Name == "dolbyDb4").Value == 12m, "EQ keyboard adjustment reaches the positive limit");
                key.Invoke(slider, new object[] { new KeyEventArgs(Keys.Up) });
                Check(slider.Value == 1200, "EQ keyboard adjustment cannot exceed the range");
                key.Invoke(slider, new object[] { new KeyEventArgs(Keys.Home) });
                Check(slider.Value == -1200, "EQ keyboard adjustment reaches the negative limit");
                Descendants(editor).OfType<CheckBox>().Single(c => c.Name == "dolbyUseEq").Checked = false;
                key.Invoke(slider, new object[] { new KeyEventArgs(Keys.Up) });
                Check(slider.Value == -1200 && editor.Value().Eq == null, "disabled EQ sliders do not accept keyboard edits or activate EQ");
            }
            using (var form = new Form { ClientSize = new Size(552, 412) })
            using (var editor = new DolbyEditor("test", profile))
            {
                form.Controls.Add(editor); form.Show(); Application.DoEvents();
                var before = editor.Value().Eq;
                var slider = Descendants(editor).OfType<LevelSlider>().Single(s => s.Name == "dolbySlider1");
                editor.AutoScrollPosition = new Point(0, 600); slider.Focus(); Application.DoEvents();
                // A pointer press on the thumb must not quantize a loaded 1/16 dB value.
                int thumbY = 11 + (int)Math.Round((1 - (slider.Value + 1200) / 2400F) * (slider.Height - 22));
                int location = (thumbY << 16) | (slider.Width / 2);
                SendMessage(slider.Handle, 0x0201, new IntPtr(1), new IntPtr(location));
                SendMessage(slider.Handle, 0x0200, new IntPtr(1), new IntPtr(location));
                SendMessage(slider.Handle, 0x0202, IntPtr.Zero, new IntPtr(location));
                Check(editor.Value().Eq.SequenceEqual(before), "pressing and releasing an EQ thumb without movement preserves the exact raw curve");
                SendMessage(slider.Handle, 0x0201, new IntPtr(1), new IntPtr(location));
                SendMessage(slider.Handle, 0x0200, new IntPtr(1), new IntPtr((11 << 16) | (slider.Width / 2)));
                SendMessage(slider.Handle, 0x0202, IntPtr.Zero, new IntPtr((11 << 16) | (slider.Width / 2)));
                Check(slider.Value >= 1190, "pointer dragging an EQ thumb reaches the upper end without losing capture");
                var after = editor.Value().Eq;
                editor.AutoScrollPosition = Point.Empty;
                SendMessage(editor.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                Check(editor.AutoScrollPosition.Y < 0 && editor.Value().Eq.SequenceEqual(after), "themed editor scrollbar responds to wheel without modifying EQ");
                form.Close();
            }
            foreach (bool fail in new[] { false, true })
            using (var form = new Form())
            {
                var source = new TaskCompletionSource<DolbyResult>();
                var editor = new DolbyEditor("test", profile, () => source.Task); form.Controls.Add(editor); form.Show();
                var read = Descendants(editor).OfType<Button>().Single(b => b.Name == "readDolby"); read.PerformClick();
                Check(editor.Busy && !Descendants(editor).OfType<CheckBox>().Single(c => c.Name == "useDolby").Enabled &&
                    !Descendants(editor).OfType<NumberBox>().Single(n => n.Name == "dolbyDb0").Enabled, "pending Dolby read locks edits so delayed results cannot overwrite newer user input");
                var replacement = new DolbyProfile { MainProfile = 4, SubProfile = 5, Eq = Enumerable.Repeat(16, 20).ToArray() };
                source.SetResult(fail ? new DolbyResult() : new DolbyResult { Profile = replacement });
                var watch = Stopwatch.StartNew(); while (editor.Busy && watch.ElapsedMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(1); }
                Check(!editor.Busy && read.Enabled && Wire.Encode(editor.Value()) == Wire.Encode(fail ? profile : replacement),
                    fail ? "empty Dolby read preserves the entire original profile and unlocks editing" : "successful Dolby read fills the new profile and unlocks editing");
                form.Close();
            }
        }
        private static void DolbySmoke()
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe");
            using (var process = Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false, CreateNoWindow = true }))
            {
                string backup = null, id = null; DolbyProfile original = null;
                try
                {
                    Reply initial = null;
                    for (int i = 0; i < 15; i++) { try { initial = Wire.Send(new Request { Action = "snapshot" }); break; } catch { Thread.Sleep(100); } }
                    Check(initial != null && initial.BackendPid == process.Id, "Dolby smoke owns isolated backend process");
                    WaitDolby(); initial = Wire.Send(new Request { Action = "snapshot" });
                    id = initial.State.Default(0, 1);
                    Check(Enumerable.Range(0, 3).All(role => initial.State.Default(0, role) == id), "Dolby smoke preserves existing identical output roles");
                    backup = Wire.Send(new Request { Action = "exportSettings" }).ConfigurationJson;
                    var read = DolbyWorker.Run(id, null); Check(read.Error == null, "production worker captures real default device Dolby state"); original = read.Profile;
                    var save = Wire.Send(new Request { Action = "saveDeviceSettings", DeviceId = id, Profile = new DeviceProfile { Dolby = new DolbyProfile { MainProfile = 0 } } });
                    Check(save.Error == null && !save.DolbyApplying, "save-only Dolby profile does not schedule audio writes");
                    Check(Wire.Encode(DolbyWorker.Run(id, null).Profile) == Wire.Encode(original), "save-only preserves every live Dolby setting");
                    var apply = Wire.Send(new Request { Action = "switch", DeviceId = id });
                    Check(apply.Error == null, "normal device-selection path queues Dolby application");
                    var done = WaitDolby();
                    Check(done.Warning == null && DolbyWorker.Run(id, null).Profile.MainProfile == 0, "backend actually applies per-device movie preset without Access UI");
                    Check(done.FrontendPid == 0 && done.PromptPid == 0, "background Dolby application requires no frontend or popup");
                    Check(initial.State.Defaults.All(p => done.State.Defaults[p.Key] == p.Value), "Dolby preset application leaves all existing default roles unchanged");
                }
                finally
                {
                    try
                    {
                        if (backup != null) { WaitDolby(); var restored = Wire.Send(new Request { Action = "importSettings", ConfigurationJson = backup }); Check(restored.Error == null, "Dolby smoke restores original user configuration"); }
                        if (original != null) { var restored = DolbyWorker.Run(id, new DolbyProfile { MainProfile = original.MainProfile, SubProfile = original.SubProfile }); Check(restored.Error == null && Wire.Encode(DolbyWorker.Run(id, null).Profile) == Wire.Encode(original), "Dolby smoke restores and independently verifies every original effect"); }
                    }
                    finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
                }
            }
        }
        private static Reply WaitDolby()
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 15000) { var snapshot = Wire.Send(new Request { Action = "snapshot" }); if (!snapshot.DolbyApplying) return snapshot; Thread.Sleep(50); }
            throw new Exception("Dolby backend timed out");
        }
        private static void TestWorkerCancellationSignal()
        {
            string name = "Local\\AudioSwitch-TestCancel-" + Guid.NewGuid().ToString("N");
            using (var signal = new EventWaitHandle(true, EventResetMode.ManualReset, name))
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe"), "--dolby-worker") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, StandardOutputEncoding = new System.Text.UTF8Encoding(false) }))
            {
                try
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    process.StandardInput.WriteLine(Wire.Encode(new DolbyRequest { DeviceId = "deliberately-invalid", Profile = new DolbyProfile { Enabled = false }, CancellationEvent = name, OwnerPid = Process.GetCurrentProcess().Id }));
                    process.StandardInput.Close();
                    Check(process.WaitForExit(5000) && output.Wait(1000), "cancelled native worker exits within deadline without opening UI");
                    var result = Wire.Decode<DolbyResult>(output.Result);
                    Check(result.Error != null && result.Error.Contains("任务已取消"), "cross-process signal cancels worker before endpoint/native Dolby access: " + result.Error);
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
            }
        }
        private static Endpoint Device(string id, int flow) { return new Endpoint { Id = id, Name = id, Flow = flow }; }
        private static AudioState State(string output, string input, params Endpoint[] devices)
        {
            var state = new AudioState { Devices = devices.ToList() };
            for (int role = 0; role < 3; role++)
            {
                if (output != null) state.Defaults["0:" + role] = output;
                if (input != null) state.Defaults["1:" + role] = input;
            }
            return state;
        }
        private static void RunStateTests()
        {
            var speaker = Device("speaker", 0); var headset = Device("headset", 0); var mic = Device("mic", 1);
            var tracker = new ArrivalTracker(State("speaker", null, speaker));
            Check(tracker.Pending.Count == 0, "startup does not prompt for existing endpoints");
            Check(tracker.Update(State("headset", "mic", speaker, headset, mic), true), "arrival detected even when Windows auto-switches defaults");
            Check(tracker.Pending.Count == 2, "USB output and input prompts are both retained");
            var output = tracker.Pending.Single(p => p.Flow == 0);
            Check(output.PreviousDefaults["0:1"] == "speaker", "pre-arrival default retained");
            Check(!tracker.Update(State("headset", "mic", speaker, headset, mic), true), "duplicate notifications do not create prompts");
            Check(tracker.Pending.Count == 2, "duplicate prompts suppressed");
            tracker.Update(State("speaker", "mic", speaker, mic), true);
            Check(tracker.Pending.Count == 1 && tracker.Pending[0].Flow == 1, "disconnect removes stale pending endpoint");
            tracker.Update(State("speaker", "mic", speaker, headset, mic), true);
            Check(tracker.Pending.Count == 2, "reconnect prompts again");
            output = tracker.Pending.Single(p => p.Flow == 0);
            var another = Device("monitor", 0);
            tracker.Update(State("monitor", "mic", speaker, headset, mic, another), true);
            Check(tracker.Pending.Single(p => p.Flow == 0).NewDevices.Count == 2, "rapid arrivals coalesce without dropping choices");
            Check(output.PreviousDefaults["0:1"] == "speaker", "coalescing preserves original default");
            tracker.Dismiss(output.Token);
            Check(tracker.Pending.Count == 1, "dismiss affects only its own flow");
            tracker.Update(State("speaker", null, speaker), true);
            Check(tracker.Pending.Count == 0, "all disconnected prompts cleaned up");
            tracker.Update(State("headset", null, speaker, headset), false);
            Check(tracker.Pending.Count == 0, "disabled prompt preference respected");
            var empty = new ArrivalTracker(new AudioState());
            empty.Update(State("headset", null, headset), true);
            Check(empty.Pending.Count == 1 && empty.Pending[0].PreviousDefaults.Count == 0, "first device from an empty system handled");
            var roundtrip = Wire.Decode<AudioState>(Wire.Encode(tracker.Current));
            Check(roundtrip.Default(0, 1) == "headset" && roundtrip.Devices.Count == 2, "IPC state serialization round trip");
            tracker = new ArrivalTracker(State("speaker", null, speaker, headset));
            Check(!tracker.Update(State("headset", null, speaker, headset), true) && tracker.Pending.Count == 0, "manual default-only change does not prompt");
        }
        private static void Render()
        {
            Application.EnableVisualStyles();
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(output);
            var speaker = Device("Realtek 扬声器", 0); var headphones = Device("WH-1000XM5 耳机", 0); var mic = Device("USB 桌面麦克风", 1);
            var initial = State(speaker.Id, mic.Id, speaker, mic);
            var state = State(speaker.Id, mic.Id, speaker, headphones, mic);
            var tracker = new ArrivalTracker(initial); tracker.Update(state, true);
            using (var window = new Dashboard(false, true))
            {
                window.Show();
                window.RenderReply(new Reply { State = state, Pending = new List<Arrival>(), Preferences = new Preferences() });
                Application.DoEvents();
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, window.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "dashboard.png")); }
                window.RenderReply(new Reply { State = state, Pending = tracker.Pending, Preferences = new Preferences() });
                Application.DoEvents();
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, window.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "new-device.png")); }
                tracker = new ArrivalTracker(State(headphones.Id, mic.Id, speaker, headphones, mic));
                tracker.Update(initial, true, true);
                window.RenderReply(new Reply { State = initial, Pending = tracker.Pending, Preferences = new Preferences() });
                Application.DoEvents();
                var selection = Descendants(window).OfType<ComboBox>().Single();
                Check(selection.Items.Count == 1 && ((Endpoint)selection.Items[0]).Id == speaker.Id, "disconnect UI lists online devices of the same flow only");
                Check(Descendants(window).OfType<Button>().Any(b => b.Text == "保持当前选择"), "disconnect UI has keep-current choice in priority mode");
                Check(!Descendants(window).OfType<Button>().Any(b => b.Text == "继续用旧设备"), "disconnect UI does not offer disconnected device restoration");
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, window.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "disconnected.png")); }
                tracker.Update(State(null, mic.Id, mic), true, true);
                window.RenderReply(new Reply { State = tracker.Current, Pending = tracker.Pending, Preferences = new Preferences() });
                Application.DoEvents();
                Check(!Descendants(window).OfType<ComboBox>().Any(), "no alternative selector when no device is available");
                Check(Descendants(window).OfType<Button>().Any(b => b.Text == "知道了"), "empty-device prompt can be dismissed");
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, window.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "no-devices.png")); }
                window.Close();
            }
            Check(true, "dashboard and arrival layouts rendered");
        }
        private static void TestDashboardWorkspace()
        {
            foreach (int flow in new[] { 0, 1 })
            foreach (int size in new[] { 44, 66, 88 })
            using (var glyph = new DeviceGlyph { Flow = flow, Active = true, Size = new Size(size, size) })
            using (var bitmap = new Bitmap(size, size)) {
                glyph.DrawToBitmap(bitmap, glyph.ClientRectangle);
                int left = size, top = size, right = -1, bottom = -1;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
                    Color pixel = bitmap.GetPixel(x, y), ink = Palette.Accent;
                    if (Math.Abs(pixel.R - ink.R) + Math.Abs(pixel.G - ink.G) + Math.Abs(pixel.B - ink.B) > 35) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
                Check(right > left && Math.Abs((left + right) / 2F - size / 2F) <= 1 && Math.Abs((top + bottom) / 2F - size / 2F) <= 1,
                    "device symbol is centered inside its tile for flow " + flow + " at " + size + " px");
            }
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            var devices = Enumerable.Range(0, 12).Select(i => new Endpoint { Id = "workspace-" + i, Flow = i < 7 ? 0 : 1,
                Name = i < 2 ? "同名耳机" : "USB 音频设备 " + i + " · 用于检查超长名称在窄窗口下是否遮挡操作按钮" }).ToArray();
            var reply = new Reply { State = State(devices[0].Id, devices[7].Id, devices), Pending = new List<Arrival>(), Preferences = new Preferences() };
            var requests = new List<Request>();
            using (var window = new Dashboard(false, false, request => { requests.Add(request); return Task.FromResult(reply); }))
            {
                window.Show(); Application.DoEvents();
                var inputFilter = Descendants(window).OfType<Button>().Single(b => b.Name == "filter2");
                int writes = requests.Count(r => r.Action != "snapshot"); inputFilter.PerformClick();
                Check(Descendants(window).Count(c => c.Name == "deviceRow") == 5 && !Descendants(window).Any(c => c.Name == "deviceGroup0"), "input filter shows only microphones");
                window.RenderReply(reply);
                Check(Descendants(window).Count(c => c.Name == "deviceRow") == 5, "snapshot refresh preserves device filter");
                Check(requests.Count(r => r.Action != "snapshot") == writes, "changing device filter never sends an audio or preference write");
                Descendants(window).OfType<Button>().Single(b => b.Name == "filter1").PerformClick();
                Check(Descendants(window).Count(c => c.Name == "deviceRow") == 7, "output filter includes every online output including duplicate names");
                var duplicateRows = Descendants(window).Where(c => c.Name == "deviceRow" && c.Controls.OfType<Label>().Any(l => l.Text == "同名耳机")).ToArray();
                duplicateRows.SelectMany(c => c.Controls.OfType<Button>()).Single(b => b.Name == "switchDevice" && b.Enabled).PerformClick();
                Check(requests.Last().Action == "switch" && requests.Last().DeviceId == devices[1].Id, "redesigned switch action targets endpoint ID even when display names match");
                window.Size = window.MinimumSize; Application.DoEvents();
                Check(Descendants(window).OfType<DeviceGlyph>().All(g => Math.Abs(g.Top * 2 + g.Height - g.Parent.ClientSize.Height) <= 1), "device icon tiles stay vertically centered in dashboard rows");
                Check(Descendants(window).Where(c => c.Name == "deviceRow").All(row => row.Controls.OfType<Button>().All(b => row.ClientRectangle.Contains(b.Bounds)) &&
                    row.Controls.OfType<Label>().All(l => row.Controls.OfType<Button>().All(b => !l.Bounds.IntersectsWith(b.Bounds)))), "minimum window width keeps long names clear of switch and settings buttons");
                var scroller = Descendants(window).OfType<Panel>().Single(c => c.Name == "deviceList");
                Check(scroller.VerticalScroll.Visible && !scroller.HorizontalScroll.Visible, "many devices scroll vertically without a horizontal scrollbar");
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, window.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "dashboard-compact.png")); }
                var tracker = new ArrivalTracker(State(devices[0].Id, devices[7].Id, devices));
                var added = Device("新麦克风", 1);
                tracker.Update(State(devices[0].Id, devices[7].Id, devices.Concat(new[] { added }).ToArray()), true);
                reply.State = tracker.Current; reply.Pending = tracker.Pending; window.RenderReply(reply);
                Check(Descendants(window).OfType<Button>().Any(b => b.Text == "使用新设备"), "output filter does not hide a pending microphone arrival");
                Descendants(window).OfType<Button>().Single(b => b.Name == "filter0").PerformClick();
                Check(Descendants(window).Count(c => c.Name.StartsWith("deviceGroup")) == 2, "all-devices filter restores both device groups");
                window.Close();
            }
        }
        private static void SaveUi(Form form, string file)
        {
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, form.ClientRectangleWithFrame());
                bitmap.Save(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", file)));
            }
        }
        private static void TestNoticesAndMenus()
        {
            foreach (int size in new[] { 16, 20, 24, 32 })
            using (var icon = AppIcon.Create(size))
            using (var bitmap = icon.ToBitmap()) {
                Check(icon.Width == size && icon.Height == size, "tray icon is rendered at native size " + size);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "tray-icon-" + size + ".png"));
            }
            foreach (bool dark in new[] { false, true })
            {
                Palette.Apply(dark); string suffix = dark ? "dark" : "light";
                using (var notice = new NoticeDialog("操作未完成", "无法连接到音频设备。\n\n请确认设备仍已连接，再重试刚才的操作。", true, false)) {
                    notice.Show(); SaveUi(notice, "notice-error-" + suffix + ".png");
                    Check(notice.AcceptButton == notice.CancelButton && Descendants(notice).OfType<Button>().All(b => notice.ClientRectangle.Contains(b.Bounds)), "error notice keeps close and copy actions visible in " + suffix);
                    notice.Close();
                }
                using (var notice = new NoticeDialog("导入设置", String.Join("\n", Enumerable.Repeat("设备预设和白名单将随备份恢复；导入前保留原配置。", 35)), false, true)) {
                    notice.Show(); Application.DoEvents();
                    var body = Descendants(notice).OfType<ScrollCanvas>().Single(); body.ScrollBy(10000);
                    Check(body.AutoScrollPosition.Y < 0 && notice.CancelButton.DialogResult == DialogResult.Cancel && notice.ActiveControl == notice.CancelButton,
                        "long confirmation scrolls while cancel remains the initial action in " + suffix);
                    SaveUi(notice, "notice-confirm-" + suffix + ".png");
                    notice.CancelButton.PerformClick(); Check(notice.DialogResult == DialogResult.Cancel, "cancelling the styled confirmation returns Cancel"); notice.Close();
                }
                foreach (float textScale in new[] { 1F, 1.5F, 2F })
                using (var scaledMenuFont = new Font("Microsoft YaHei UI", 9F * textScale))
                using (var host = new Form { ClientSize = new Size(700, 430), BackColor = Palette.Background })
                using (var menu = new ThemedMenu()) {
                    menu.Font = scaledMenuFont;
                    string menuSuffix = suffix + (textScale == 1F ? "" : "-" + (int)(textScale * 100));
                    int commands = 0;
                    menu.Items.Add(new TrayMenuHeading()); menu.Items.Add("打开管理面板", null, delegate { commands++; });
                    menu.Items.Add(new ToolStripSeparator());
                    var group = new TrayDeviceGroup("声音输出", "耳机 (Realtek Audio)") { DropDown = new ThemedMenu() };
                    group.DropDown.Font = scaledMenuFont;
                    group.DropDownItems.Add(new ToolStripMenuItem("耳机 (Realtek Audio)") { Checked = true });
                    group.DropDownItems.Add(new ToolStripMenuItem("USB 音频设备 · 很长的设备名称，用于检查菜单省略和完整悬停说明"));
                    group.DropDownItems.Add(new ToolStripMenuItem("暂无可用设备") { Enabled = false }); menu.Items.Add(group);
                    menu.Items.Add(new TrayDeviceGroup("麦克风输入", "USB 麦克风")); menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(new ToolStripMenuItem("按设备优先级自动选择") { Checked = true }); menu.Items.Add("退出");
                    host.Show(); menu.Show(host, new Point(20, 20)); Application.DoEvents();
                    using (var graphics = menu.CreateGraphics()) {
                        int line = (int)Math.Ceiling(scaledMenuFont.GetHeight(graphics));
                        Check(group.Height >= line * 2 + 6 * textScale, "tray device rows fit two readable lines at text scale " + textScale + " in " + suffix);
                    }
                    Check(menu.Items.Cast<ToolStripItem>().Where(item => item.Available).All(item => menu.ClientRectangle.Contains(item.Bounds)),
                        "tray items stay inside the menu at text scale " + textScale + " in " + suffix);
                    Check(menu.Items.OfType<ToolStripMenuItem>().All(item => menu.Width - item.Bounds.Right <= menu.Padding.Right + 3),
                        "tray hit areas extend to the menu edge without an empty submenu gap in " + suffix);
                    using (var bitmap = new Bitmap(menu.Width, menu.Height)) { menu.DrawToBitmap(bitmap, menu.ClientRectangle); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "tray-menu-" + menuSuffix + ".png")); }
                    group.ShowDropDown(); Application.DoEvents();
                    Check(group.DropDownItems.Cast<ToolStripItem>().All(item => group.DropDown.ClientRectangle.Contains(item.Bounds)),
                        "long device submenu rows fit the popup rather than painting past its edge at text scale " + textScale + " in " + suffix);
                    using (var bitmap = new Bitmap(group.DropDown.Width, group.DropDown.Height)) { group.DropDown.DrawToBitmap(bitmap, group.DropDown.ClientRectangle); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "tray-devices-" + menuSuffix + ".png")); }
                    Check(commands == 0 && ((ToolStripMenuItem)group.DropDownItems[0]).Checked && !group.DropDownItems[2].Enabled, "opening themed tray menus preserves check states and runs no commands in " + suffix);
                    group.HideDropDown(); menu.Items[1].PerformClick(); Check(commands == 1, "tray command still activates exactly once");
                    menu.Close();
                    var originalMenuSize = menu.Size;
                    var workArea = Screen.FromControl(host).WorkingArea;
                    menu.Show(new Point(workArea.Right - 4, workArea.Bottom - 4)); Application.DoEvents();
                    Check(menu.Size == originalMenuSize && workArea.Contains(menu.Bounds), "reopening tray menu at the screen edge keeps stable size and stays visible at text scale " + textScale + " in " + suffix);
                    menu.Close(); host.Close();
                }
            }
            Palette.Apply(false);
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct UiRect { internal int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ComboInfo { internal int Size; internal UiRect Item, Button; internal int State; internal IntPtr Combo, Edit, List; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr combo, ref ComboInfo info);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out UiRect rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetUpdateRect(IntPtr window, out UiRect rect, bool erase);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowStyle(IntPtr window, int index);
        private static void TestDropdownHover(ChoiceBox choice)
        {
            var info = new ComboInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ComboInfo)) }; UiRect bounds;
            if (!GetComboBoxInfo(choice.Handle, ref info) || !GetWindowRect(info.List, out bounds)) throw new Exception("Cannot inspect dropdown");
            foreach (int row in new[] { 0, 2, 0, 1 })
            {
                // Exercise the native popup's pointer path, including leaving a hovered row.
                int y = row * choice.ItemHeight + choice.ItemHeight / 2;
                SendMessage(info.List, 0x0200, IntPtr.Zero, new IntPtr((y << 16) | 20));
                Application.DoEvents();
                using (var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr dc = graphics.GetHdc(); try { PrintWindow(info.List, dc, 0); } finally { graphics.ReleaseHdc(dc); }
                    var checkedRows = new List<int>();
                    for (int i = 0; i < choice.Items.Count; i++)
                    {
                        int pixels = 0;
                        for (int x = bitmap.Width - 27; x < bitmap.Width - 7; x++)
                        for (int py = 2 + i * choice.ItemHeight; py < Math.Min(bitmap.Height - 1, (i + 1) * choice.ItemHeight); py++)
                        {
                            Color color = bitmap.GetPixel(x, py), accent = Palette.Accent;
                            if (Math.Abs(color.R - accent.R) + Math.Abs(color.G - accent.G) + Math.Abs(color.B - accent.B) < 35) pixels++;
                        }
                        if (pixels >= 5) checkedRows.Add(i);
                    }
                    Check(checkedRows.SequenceEqual(new[] { 1 }), "hovering dropdown row " + row + " leaves exactly one check on the committed item");
                }
                SendMessage(info.List, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            }
            Application.DoEvents();
            int sameRowY = choice.ItemHeight + choice.ItemHeight / 2;
            SendMessage(info.List, 0x0200, IntPtr.Zero, new IntPtr((sameRowY << 16) | 22));
            Application.DoEvents();
            UiRect dirty;
            SendMessage(info.List, 0x0200, IntPtr.Zero, new IntPtr((sameRowY << 16) | 24));
            Check(!GetUpdateRect(info.List, out dirty, false) || dirty.Bottom - dirty.Top <= choice.ItemHeight * 2,
                "pointer motion within one option does not invalidate the full dropdown");
            SendMessage(choice.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
            Check(choice.SelectedIndex == 1, "leaving dropdown choices without clicking does not commit a new value");
        }
        private static void SaveDropdown(ComboBox choice, string file)
        {
            var info = new ComboInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ComboInfo)) }; UiRect bounds;
            Check(GetComboBoxInfo(choice.Handle, ref info) && info.List != IntPtr.Zero, "native dropdown is available for visual inspection");
            Check((GetWindowStyle(info.List, -16) & 0x00C00000) == 0 && (GetWindowStyle(info.List, -20) & 0x00020300) == 0,
                "expanded list has no native rectangular or sunken border underneath the rounded outline");
            if (!GetWindowRect(info.List, out bounds)) throw new Exception("Cannot measure dropdown");
            using (var rowImage = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
            using (var rowGraphics = Graphics.FromImage(rowImage)) {
                rowGraphics.Clear(Palette.Card);
                PopupChrome.DrawBorder(rowGraphics, rowImage.Size);
                using (var firstFrame = (Bitmap)rowImage.Clone()) {
                    for (int row = 0; row < choice.Items.Count; row++) {
                        var area = new Rectangle(0, row * choice.ItemHeight, rowImage.Width, choice.ItemHeight);
                        choice.GetType().GetMethod("OnDrawItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(choice, new object[] { new DrawItemEventArgs(rowGraphics, choice.Font, area, row, DrawItemState.Selected) });
                        int center = Math.Min(rowImage.Height - 10, area.Top + area.Height / 2);
                        Check(rowImage.GetPixel(1, center) == firstFrame.GetPixel(1, center) && rowImage.GetPixel(rowImage.Width - 2, center) == firstFrame.GetPixel(rowImage.Width - 2, center),
                            "partial hover repaint preserves both dropdown border sides on row " + row);
                    }
                    bool sameRim = true;
                    for (int x = 10; x < rowImage.Width - 10; x++)
                        sameRim &= rowImage.GetPixel(x, 1) == firstFrame.GetPixel(x, 1);
                    Check(sameRim, "hovering the first row preserves the initial top outline without a whole-window repaint");
                }
                PopupChrome.DrawBorder(rowGraphics, rowImage.Size);
                using (var firstFrame = (Bitmap)rowImage.Clone()) {
                    PopupChrome.DrawBorder(rowGraphics, rowImage.Size);
                    PopupChrome.DrawBorder(rowGraphics, rowImage.Size);
                    bool stable = true;
                    for (int y = 0; y < rowImage.Height; y++) for (int x = 0; x < rowImage.Width; x++)
                        if (x < 10 || x >= rowImage.Width - 10 || y < 4 || y >= rowImage.Height - 4)
                            stable &= rowImage.GetPixel(x, y) == firstFrame.GetPixel(x, y);
                    Check(stable, "repeated dropdown border paints do not darken antialiased corners");
                }
            }
            using (var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr dc = graphics.GetHdc();
                try { Check(PrintWindow(info.List, dc, 0), "native dropdown renders for visual inspection"); }
                finally { graphics.ReleaseHdc(dc); }
                for (int row = 0; row < choice.Items.Count; row++) {
                    int textPixels = 0;
                    for (int y = row * choice.ItemHeight + 2; y < Math.Min(bitmap.Height - 1, (row + 1) * choice.ItemHeight); y++)
                    for (int x = 10; x < bitmap.Width - 40; x++) {
                        Color pixel = bitmap.GetPixel(x, y), foreground = Palette.Text;
                        if (Math.Abs(pixel.R - foreground.R) + Math.Abs(pixel.G - foreground.G) + Math.Abs(pixel.B - foreground.B) < 45) textPixels++;
                    }
                    Check(textPixels > 8, "buffered dropdown keeps visible text on row " + row);
                }
                bitmap.Save(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", file)));
            }
        }
        private static void TestThemeAndControls()
        {
            foreach (bool dark in new[] { false, true })
            {
                Palette.Apply(dark);
                using (var parent = new Surface())
                foreach (Control control in new Control[] { new TickOption { Text = "应用此音量", Checked = true }, new FlatAction("仅保存", false) })
                using (control)
                using (var bitmap = new Bitmap(220, 36))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    parent.Controls.Add(control); control.Size = bitmap.Size;
                    graphics.Clear(Color.Magenta);
                    control.GetType().GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(control, new object[] { new PaintEventArgs(graphics, control.ClientRectangle) });
                    Check(bitmap.GetPixel(0, 0).ToArgb() == parent.Fill.ToArgb() && bitmap.GetPixel(219, 35).ToArgb() == parent.Fill.ToArgb(),
                        control.GetType().Name + " clears stale pixels across its background in " + (dark ? "dark" : "light") + " mode");
                }
            }
            Palette.Apply(false);
            var speaker = Device("桌面扬声器", 0); var headset = Device("蓝牙耳机", 0); var mic = Device("USB 麦克风", 1);
            var prefs = new Preferences { DeviceOrder = new List<Endpoint> { speaker, headset, mic } };
            var reply = new Reply { Preferences = prefs, State = State(speaker.Id, mic.Id, speaker, headset, mic), Pending = new List<Arrival>() };
            var requests = new List<Request>(); bool fail = false;
            using (var dashboard = new Dashboard(false, false, request => {
                requests.Add(request);
                if (request.Action == "darkMode") { if (!fail) prefs.DarkMode = request.Value; reply.Error = fail ? "主题保存失败，请重试" : null; }
                return Task.FromResult(reply);
            }))
            {
                dashboard.Show();
                Descendants(dashboard).OfType<Button>().Single(b => b.Name == "filter1").PerformClick();
                var toggle = Descendants(dashboard).OfType<CheckBox>().Single(c => c.Name == "darkMode");
                toggle.Checked = true;
                Check(prefs.DarkMode && Palette.Dark && requests.Last().Action == "darkMode", "theme switch sends only the dedicated appearance request and applies saved result");
                Check(dashboard.BackColor == Palette.Background && Descendants(dashboard).OfType<Label>().Any(l => l.Text == "声音设备" && l.ForeColor == Palette.Text), "open dashboard updates backgrounds and text together in dark mode");
                Check(!Descendants(dashboard).Any(c => c.Name == "deviceGroup1"), "theme change preserves the active device filter");
                Descendants(dashboard).OfType<Button>().Single(b => b.Name == "filter0").PerformClick();
                SaveUi(dashboard, "dashboard-dark.png");
                var info = new DeviceSettingsInfo { Device = headset, CurrentVolume = 65, Profile = new DeviceProfile { Volume = 40, Dolby = DolbyProfiles.Capture(new FakeDolby()) },
                    Spatial = new SpatialState { CurrentFormat = "", Supported = true, Options = new List<SpatialOption> { new SpatialOption { Id = "", Name = "关闭空间音效" }, new SpatialOption { Id = FakeSettings.Sonic, Name = "Windows Sonic（耳机）" } } } };
                using (var editor = new DeviceSettingsDialog(info, request => Task.FromResult(new Reply())))
                {
                    editor.Show();
                    var number = Descendants(editor).OfType<NumberBox>().Single(n => n.Name == "volumeNumber"); number.Value = 47;
                    var spatial = Descendants(editor).OfType<ChoiceBox>().Single(c => c.Name == "spatial"); spatial.SelectedIndex = 2;
                    Check(((SpatialOption)spatial.SelectedItem).Id == FakeSettings.Sonic && spatial.Text.Contains("Windows Sonic"), "styled selector preserves display binding and selected format identity");
                    SaveUi(editor, "device-settings-dark.png");
                    Palette.Apply(false); Palette.Apply(true);
                    Check(number.Value == 47 && spatial.SelectedIndex == 2 && editor.BackColor == Palette.Background, "changing theme preserves unsaved editor values and selection");
                    var sections = Descendants(editor).OfType<SectionTabs>().Single(); sections.SelectedIndex = 1;
                    SaveUi(editor, "dolby-settings-dark.png");
                    Descendants(editor).OfType<DolbyEditor>().Single().AutoScrollPosition = new Point(0, 600);
                    SaveUi(editor, "dolby-eq-dark.png"); editor.Close();
                }
                using (var priority = new DevicePriorityDialog(0, reply, request => Task.FromResult(new Reply())))
                {
                    priority.Show(); var list = Descendants(priority).OfType<PriorityList>().Single(); list.SelectedIndex = 1;
                    Descendants(priority).OfType<Button>().Single(b => b.Name == "moveUp").PerformClick();
                    Check(list.SelectedIndex == 0 && list.Items[0].ToString() == headset.Name, "styled priority list preserves selected item while reordering");
                    SaveUi(priority, "device-priority-dark.png"); priority.Close();
                }
                var tracker = new ArrivalTracker(State(speaker.Id, mic.Id, speaker, mic)); tracker.Update(reply.State, true);
                using (var prompt = new DevicePrompt(request => Task.FromResult(reply), true, new Reply { State = reply.State, Preferences = prefs, Pending = tracker.Pending }))
                {
                    prompt.Show(); SaveUi(prompt, "toast-dark.png");
                    Check(prompt.BackColor == Palette.Sidebar && Descendants(prompt).OfType<ChoiceBox>().Any(), "actionable prompt uses saved dark theme and styled device selection"); prompt.Close();
                }
                fail = true; toggle.Checked = false;
                Check(toggle.Checked && Palette.Dark && prefs.DarkMode, "failed theme save restores the toggle and retains the saved appearance");
                fail = false; toggle.Checked = false;
                Check(!Palette.Dark && dashboard.BackColor == Palette.Background && !prefs.DarkMode, "switching back to light recolors the existing window");
                Check(requests.All(r => r.Action == "snapshot" || r.Action == "darkMode"), "theme switching and editor appearance checks never request audio writes");
                dashboard.Close();
            }
            foreach (bool dark in new[] { false, true })
            {
                Palette.Apply(dark);
                using (var sample = new Form { Text = "组件状态 · " + (dark ? "深色" : "浅色"), ClientSize = new Size(670, 350), BackColor = Palette.Background, Font = new Font("Microsoft YaHei UI", 9F) })
                {
                    string[] labels = { "主要操作", "普通操作", "当前选中", "不可用" };
                    for (int i = 0; i < 4; i++)
                    {
                        var button = new FlatAction(labels[i], i == 0) { Selected = i == 2, Enabled = i != 3 }; button.SetBounds(24 + i * 160, 28, 142, 38); sample.Controls.Add(button);
                    }
                    var choice = new ChoiceBox(); choice.Items.AddRange(new[] { "保持当前选择", "Windows Sonic（耳机）", "Dolby Atmos（耳机）" }); choice.SelectedIndex = 1; choice.SetBounds(24, 88, 300, 32); sample.Controls.Add(choice);
                    var list = new PriorityList { IsOnline = i => i != 2 }; list.SetBounds(24, 142, 622, 180); list.Items.AddRange(new[] { "桌面扬声器", "蓝牙耳机", "USB 备用音频设备" }); list.SelectedIndex = 1; sample.Controls.Add(list);
                    sample.Show(); Palette.ChromeTree(sample); choice.Focus(); SaveUi(sample, "components-" + (dark ? "dark" : "light") + ".png");
                    choice.DroppedDown = true; Application.DoEvents();
                    Check(choice.DroppedDown && choice.SelectedIndex == 1, "opening styled dropdown keeps current selection in " + (dark ? "dark" : "light") + " mode");
                    SaveDropdown(choice, "dropdown-" + (dark ? "dark" : "light") + ".png");
                    TestDropdownHover(choice);
                    choice.DroppedDown = false; sample.Close();
                }
            }
            Palette.Apply(false);
        }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        private static void TestDevicePrompt()
        {
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            var speaker = Device("Realtek 扬声器", 0); var headset = Device("INZONE H5 游戏耳机", 0); var mic = Device("USB 麦克风", 1);
            var tracker = new ArrivalTracker(State(speaker.Id, mic.Id, speaker, mic));
            tracker.Update(State(speaker.Id, mic.Id, speaker, headset, mic), true, true);
            var state = new Reply { State = tracker.Current, Pending = tracker.Pending.ToList(), Preferences = new Preferences { UseDevicePriority = false } };
            Request captured = null;
            Func<Request, Task<Reply>> send = request => {
                captured = request;
                return Task.FromResult(new Reply { State = state.State, Pending = new List<Arrival>(), Preferences = state.Preferences });
            };
            using (var prompt = new DevicePrompt(send, true))
            {
                prompt.Show(); prompt.RenderReply(state); Application.DoEvents();
                Check(!prompt.ShowInTaskbar && prompt.Right <= Screen.PrimaryScreen.WorkingArea.Right && prompt.Bottom <= Screen.PrimaryScreen.WorkingArea.Bottom, "actionable prompt is placed inside bottom-right work area without taskbar entry");
                Check(prompt.Width <= 365 && prompt.Height <= 220, "compact arrival notification stays within 360 by 220 logical pixels at test DPI");
                Check(Descendants(prompt).OfType<Button>().Where(b => b.Visible).All(b => b.Parent.ClientRectangle.Contains(b.Bounds)), "compact prompt keeps every visible action within its panel");
                Check(((Endpoint)Descendants(prompt).OfType<ComboBox>().Single().SelectedItem).Id == headset.Id, "arrival prompt initially selects the new device");
                using (var bitmap = new Bitmap(prompt.Width, prompt.Height)) { prompt.DrawToBitmap(bitmap, prompt.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "toast-connected.png")); }
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "choose").PerformClick();
                Check(captured.Action == "alternative" && captured.DeviceId == headset.Id && captured.Token == state.Pending[0].Token, "new-device notification button sends correct actionable request");
                Check(prompt.IsDisposed, "notification closes after last successful choice");
            }
            int firstFrameRequests = 0;
            using (var prompt = new DevicePrompt(request => { firstFrameRequests++; return Task.FromResult(state); }, true, state))
            {
                prompt.Show();
                Check(Descendants(prompt).OfType<Button>().Any(b => b.Name == "choose") && firstFrameRequests == 0,
                    "preloaded notification has actionable content on first show without a second IPC request");
                prompt.Close();
            }
            using (var prompt = new DevicePrompt(send, true))
            {
                prompt.Show(); prompt.RenderReply(state);
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "keepOld").PerformClick();
                Check(captured.Action == "old" && captured.Token == state.Pending[0].Token, "keep-old notification button sends restore request");
            }
            tracker = new ArrivalTracker(State(headset.Id, mic.Id, speaker, headset, mic));
            tracker.Update(State(speaker.Id, mic.Id, speaker, mic), true, true);
            state = new Reply { State = tracker.Current, Pending = tracker.Pending.ToList(), Preferences = new Preferences() };
            using (var prompt = new DevicePrompt(send, true))
            {
                prompt.Show(); prompt.RenderReply(state); Application.DoEvents();
                Check(!Descendants(prompt).OfType<Button>().Any(b => b.Name == "keepOld"), "disconnected notification omits unavailable old-device operation");
                using (var bitmap = new Bitmap(prompt.Width, prompt.Height)) { prompt.DrawToBitmap(bitmap, prompt.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "toast-disconnected.png")); }
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "keepSystem").PerformClick();
                Check(captured.Action == "later", "keep-system notification button dismisses without switching");
            }
            using (var prompt = new DevicePrompt(request => { captured = request; return Task.FromResult(state); }, true))
            {
                prompt.Show(); prompt.RenderReply(state);
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "choose").PerformClick();
                Check(captured.Action == "alternative" && captured.DeviceId == speaker.Id, "disconnect notification button targets selected available device");
                prompt.Close();
            }
            using (var prompt = new DevicePrompt(request => { var failure = Wire.Decode<Reply>(Wire.Encode(state)); failure.Error = "设备已断开，请重新选择"; return Task.FromResult(failure); }, true))
            {
                prompt.Show(); prompt.RenderReply(state);
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "choose").PerformClick();
                Check(!prompt.IsDisposed && Descendants(prompt).OfType<Label>().Any(l => l.Visible && l.Text.Contains("请重新选择")), "failed notification action remains visible with error");
                Check(Descendants(prompt).OfType<Button>().Where(b => b.Visible).All(b => b.Parent.ClientRectangle.Contains(b.Bounds)), "error expansion preserves compact notification actions");
                prompt.RenderReply(new Reply { State = state.State, Pending = new List<Arrival>(), Preferences = state.Preferences });
                Check(prompt.IsDisposed, "resolved notification closes even after a stale action error");
            }
            tracker.Update(State(null, mic.Id, mic), true, true); state.State = tracker.Current; state.Pending = tracker.Pending.ToList();
            using (var prompt = new DevicePrompt(send, true))
            {
                prompt.Show(); prompt.RenderReply(state); Application.DoEvents();
                Check(!Descendants(prompt).OfType<ComboBox>().Any(), "empty-device notification has no stale selectable target");
                Check(prompt.Height < 211, "no-device notification shrinks with its content");
                using (var bitmap = new Bitmap(prompt.Width, prompt.Height)) { prompt.DrawToBitmap(bitmap, prompt.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "toast-empty.png")); }
                prompt.Close();
            }
            var inputTracker = new ArrivalTracker(State(null, mic.Id, mic)); inputTracker.Update(new AudioState(), true, true);
            state.Pending.Add(inputTracker.Pending[0]);
            using (var prompt = new DevicePrompt(request => { state.Pending.RemoveAll(p => p.Token == request.Token); return Task.FromResult(state); }, true))
            {
                prompt.Show(); prompt.RenderReply(state);
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "keepSystem").PerformClick();
                Check(!prompt.IsDisposed && Descendants(prompt).OfType<Label>().Any(l => l.Text == "当前输入设备已断开"), "notification advances to remaining input choice");
                Descendants(prompt).OfType<Button>().Single(b => b.Name == "keepSystem").PerformClick();
                Check(prompt.IsDisposed, "notification closes after all queued choices are resolved");
            }
        }
        private static void RunDisconnectionTests()
        {
            var speaker = Device("speaker", 0); var headset = Device("headset", 0); var mic = Device("mic", 1);
            var tracker = new ArrivalTracker(State("headset", "mic", speaker, headset, mic));
            Check(tracker.Update(State("speaker", "mic", speaker, mic), true, true), "current device disconnect prompts even after Windows falls back");
            var pending = tracker.Pending.Single();
            Check(pending.IsDisconnection && pending.DisconnectedDevices.Single().Id == "headset", "disconnected device identity retained");
            Check(tracker.Current.Default(0, 1) == "speaker", "system fallback is reflected without modification");
            Check(!tracker.Update(State("speaker", "mic", speaker, mic), true, true) && tracker.Pending.Count == 1, "repeated disconnect callbacks are deduplicated");
            tracker.Update(State(null, "mic", mic), true, true);
            Check(tracker.Pending.Count == 1 && tracker.Pending[0].DisconnectedDevices[0].Id == "speaker", "fallback disconnect replaces prompt rather than accumulating stale dialogs");
            Check(tracker.Pending[0].Token != pending.Token, "replacement prompt invalidates stale clicks");
            Check(!tracker.Update(State(null, "mic", mic), true, true) && tracker.Pending.Count == 1, "no-device prompt persists without repeated notification");
            tracker.Update(State("speaker", "mic", speaker, mic), true, true);
            Check(tracker.Pending.Count == 1 && !tracker.Pending[0].IsDisconnection, "reconnected device clears obsolete disconnect warning");
            tracker = new ArrivalTracker(State("speaker", "mic", speaker, headset, mic));
            Check(!tracker.Update(State("speaker", "mic", speaker, mic), true, true) && tracker.Pending.Count == 0, "unused device disconnect does not prompt");
            tracker = new ArrivalTracker(State("headset", "mic", speaker, headset, mic));
            tracker.Update(State("speaker", null, speaker), true, true);
            Check(tracker.Pending.Count == 2 && tracker.Pending.All(p => p.IsDisconnection), "input and output disconnects each remain actionable");
            tracker.Dismiss(tracker.Pending[0].Token);
            Check(tracker.Pending.Count == 1, "keeping system choice dismisses only selected prompt");
            tracker = new ArrivalTracker(State("headset", null, headset));
            tracker.Update(State("speaker", null, speaker), true, true);
            Check(tracker.Pending.Count == 1 && tracker.Pending[0].IsDisconnection && tracker.Pending[0].NewDevices.Single().Id == "speaker", "simultaneous removal and arrival merge per flow");
            tracker = new ArrivalTracker(State("headset", null, speaker, headset));
            tracker.Update(State("speaker", null, speaker), false, true);
            Check(tracker.Pending.Single().IsDisconnection, "disconnect warning independent of new-device preference");
            var calls = State("speaker", null, speaker, headset); calls.Defaults["0:2"] = "headset";
            tracker = new ArrivalTracker(calls);
            Check(!tracker.Update(State("speaker", null, speaker), true, true, false), "communications-only loss respects excluded communications setting");
            tracker = new ArrivalTracker(calls);
            Check(tracker.Update(State("speaker", null, speaker), true, true, true), "communications default loss prompts when included");
            var copy = Wire.Decode<Arrival>(Wire.Encode(tracker.Pending[0]));
            Check(copy.IsDisconnection && copy.DisconnectedDevices.Single().Id == "headset", "disconnect prompt survives IPC serialization");
        }
        private static void RunSelectionTests()
        {
            var speaker = Device("speaker", 0); var headset = Device("headset", 0);
            var current = State("speaker", null, speaker, headset);
            Func<AudioState> read = () => Wire.Decode<AudioState>(Wire.Encode(current));
            int writes = 0;
            Action<string, int> set = (id, role) => { writes++; current.Defaults["0:" + role] = id; };
            DeviceSelection.Apply(0, new Dictionary<int, string> { { 0, "headset" }, { 1, "headset" } }, read, set);
            Check(current.Default(0, 1) == "headset" && current.Default(0, 2) == "speaker", "media switch preserves communications when excluded");
            writes = 0;
            DeviceSelection.Apply(0, new Dictionary<int, string> { { 1, "headset" } }, read, set);
            Check(writes == 0, "already selected roles are not written");
            bool rejected = false;
            try { DeviceSelection.Apply(0, new Dictionary<int, string> { { 1, "gone" } }, read, set); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && writes == 0, "stale target rejected before writes");
            current = State("speaker", null, speaker, headset);
            bool failed = false;
            try
            {
                DeviceSelection.Apply(0, new Dictionary<int, string> { { 0, "headset" }, { 1, "headset" } }, read,
                    (id, role) => { if (role == 1) throw new Exception("simulated driver failure"); set(id, role); });
            }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && current.Default(0, 0) == "speaker" && current.Default(0, 1) == "speaker", "partial switch rolls successful roles back");
            failed = false;
            try { DeviceSelection.Apply(0, new Dictionary<int, string> { { 1, "headset" } }, read, (id, role) => { }); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed, "silent system rejection is detected by reading back defaults");
            string failure = null;
            try
            {
                DeviceSelection.Apply(0, new Dictionary<int, string> { { 0, "headset" }, { 1, "headset" } }, read,
                    (id, role) => { if (role == 1 || id == "speaker") throw new Exception("simulated failure"); set(id, role); });
            }
            catch (InvalidOperationException ex) { failure = ex.Message; }
            Check(failure != null && failure.Contains("部分设置无法恢复"), "rollback failure is reported honestly");
            current = State("speaker", null, speaker, headset); bool partial = true;
            try { DeviceSelection.Apply(0, new Dictionary<int, string> { { 1, "headset" } }, read,
                (id, role) => { set(id, role); if (partial) { partial = false; throw new Exception("changed then failed"); } }); } catch { }
            Check(current.Default(0, 1) == "speaker", "role write that changes state before throwing is also rolled back");
            current = State("speaker", null, speaker, headset); failure = null;
            try { DeviceSelection.Apply(0, new Dictionary<int, string> { { 0, "headset" }, { 1, "headset" } }, read,
                (id, role) => { if (id == "speaker") return; if (role == 1) throw new Exception("failed"); set(id, role); }); }
            catch (Exception ex) { failure = ex.Message; }
            Check(failure != null && failure.Contains("部分设置无法恢复"), "silent rollback rejection is detected by reading actual roles");
        }
        private static Rectangle ClientRectangleWithFrame(this Form form) { return new Rectangle(0, 0, form.Width, form.Height); }
        private static void Lifecycle()
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe");
            using (var process = Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden }))
            {
                try
                {
                    Reply snapshot = null;
                    for (int i = 0; i < 10; i++)
                    {
                        try { snapshot = Wire.Send(new Request { Action = "snapshot" }); break; }
                        catch { Thread.Sleep(250); }
                    }
                    Check(snapshot != null && snapshot.BackendPid == process.Id, "background IPC ready");
                    var exportedSettings = Wire.Send(new Request { Action = "exportSettings" });
                    Check(exportedSettings.Error == null && Wire.Encode(PreferenceStore.Parse(exportedSettings.ConfigurationJson)) == Wire.Encode(snapshot.Preferences), "live backend exports authoritative complete settings");
                    var invalidImport = Wire.Send(new Request { Action = "importSettings", ConfigurationJson = "{invalid" });
                    Check(invalidImport.Error != null && Wire.Encode(invalidImport.Preferences) == Wire.Encode(snapshot.Preferences), "live backend rejects malformed import without altering preferences");
                    var importedSettings = Wire.Send(new Request { Action = "importSettings", ConfigurationJson = exportedSettings.ConfigurationJson });
                    Check(importedSettings.Error == null && File.Exists(importedSettings.BackupPath) && Wire.Encode(importedSettings.Preferences) == Wire.Encode(snapshot.Preferences), "live import of current settings returns restorable backup and updates in-memory preferences");
                    Check(snapshot.State.Defaults.All(p => importedSettings.State.Defaults.ContainsKey(p.Key) && importedSettings.State.Defaults[p.Key] == p.Value), "settings import performs no device switching");
                    Wire.Send(new Request { Action = "show" });
                    Process ui = null;
                    for (int i = 0; i < 20; i++)
                    {
                        Thread.Sleep(200);
                        var state = Wire.Send(new Request { Action = "snapshot" });
                        if (state.FrontendPid != 0)
                        {
                            var candidate = Process.GetProcessById(state.FrontendPid);
                            if (candidate.MainWindowHandle != IntPtr.Zero) { ui = candidate; break; }
                            candidate.Dispose();
                        }
                    }
                    Check(ui != null, "separate frontend process opens");
                    using (ui)
                    {
                        Check(ui.CloseMainWindow(), "close frontend window");
                        Check(ui.WaitForExit(5000), "frontend process fully exits after close");
                    }
                    Check(!process.HasExited && Wire.Send(new Request { Action = "snapshot" }).State != null, "backend remains responsive after frontend unload");
                    process.Refresh();
                    Console.WriteLine("Backend working set: " + (process.WorkingSet64 / 1024 / 1024) + " MiB; private bytes: " + (process.PrivateMemorySize64 / 1024 / 1024) + " MiB");
                    Wire.Send(new Request { Action = "show" });
                    Thread.Sleep(1800);
                    int reopenedId = Wire.Send(new Request { Action = "snapshot" }).FrontendPid;
                    var reopened = reopenedId != 0 ? Process.GetProcessById(reopenedId) : null;
                    Check(reopened != null, "frontend can be recreated");
                    if (reopened != null) { reopened.CloseMainWindow(); reopened.WaitForExit(5000); reopened.Dispose(); }
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
            }
            using (var host = new TrayHost(false)) { host.Dispose(); host.Dispose(); }
            Check(true, "repeated tray disposal is safe");
            TestPromptProcess(exe);
        }
        private static void TestPromptProcess(string exe)
        {
            var speaker = Device("演示扬声器", 0); var headset = Device("演示耳机", 0);
            var tracker = new ArrivalTracker(State(speaker.Id, null, speaker));
            tracker.Update(State(speaker.Id, null, speaker, headset), true, true);
            var reply = new Reply { State = tracker.Current, Pending = tracker.Pending.ToList(), Preferences = new Preferences() };
            var startup = Stopwatch.StartNew();
            using (var connected = new ManualResetEventSlim())
            using (var pipe = new PipeServer(request => { connected.Set(); return Interlocked.CompareExchange(ref reply, null, null); }))
            using (var prompt = Process.Start(new ProcessStartInfo(exe, "--prompt --owner=" + Process.GetCurrentProcess().Id) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden }))
            {
                try
                {
                    Check(connected.Wait(5000) && !prompt.HasExited, "separate notification process reads pending operations through IPC");
                    Check(prompt.WaitForInputIdle(5000) && !prompt.HasExited, "preloaded notification reaches its interactive message loop");
                    Console.WriteLine("Notification startup to UI idle: " + startup.ElapsedMilliseconds + " ms (excludes device event batching)");
                    var instances = Process.GetProcessesByName("AudioSwitch");
                    try { Check(instances.Length == 1 && instances[0].Id == prompt.Id, "notification does not launch the full dashboard"); }
                    finally { foreach (var instance in instances) instance.Dispose(); }
                    Interlocked.Exchange(ref reply, new Reply { State = tracker.Current, Pending = new List<Arrival>(), Preferences = new Preferences() });
                    Check(prompt.WaitForExit(6000), "notification process fully exits when pending operations are resolved");
                }
                finally { if (!prompt.HasExited) { prompt.Kill(); prompt.WaitForExit(5000); } }
            }
        }
        private static void TestEventBatch()
        {
            int refreshes = 0;
            var watch = Stopwatch.StartNew();
            long firstRefresh = -1;
            using (var batch = new DeviceEventBatch(delegate { refreshes++; if (firstRefresh < 0) firstRefresh = watch.ElapsedMilliseconds; }))
            {
                // Simulate a continuous stream of driver callbacks; it must not defer the first refresh.
                while (firstRefresh < 0 && watch.ElapsedMilliseconds < 700)
                {
                    batch.Signal(); Application.DoEvents(); Thread.Sleep(5);
                }
                Check(firstRefresh >= 100 && firstRefresh < 400, "continuous driver events cannot postpone first refresh beyond bounded batch window");
                Console.WriteLine("First refresh during synthetic event burst: " + firstRefresh + " ms");
                long idleStart = watch.ElapsedMilliseconds;
                while (watch.ElapsedMilliseconds - idleStart < 200) { Application.DoEvents(); Thread.Sleep(5); }
                Check(refreshes == 1, "event batch does not keep polling after burst ends");
            }
        }
        private sealed class FakeSettings : IDeviceSettingsAccess
        {
            internal Dictionary<string, float> Volumes = new Dictionary<string, float>();
            internal Dictionary<string, string> Formats = new Dictionary<string, string>();
            internal List<string> Calls = new List<string>();
            internal bool FailSpatialOnce;
            internal const string Sonic = "{B53D940C-B846-4831-9F76-D102B9B725A0}";
            public float ReadVolume(string id) { Calls.Add("read-volume:" + id); return Volumes[id]; }
            public void SetVolume(string id, float value) { Calls.Add("volume:" + id); Volumes[id] = value; }
            public SpatialState ReadSpatial(string id) { return new SpatialState { Supported = true, CurrentFormat = Formats[id], Options = new List<SpatialOption> { new SpatialOption { Id = "", Name = "关闭" }, new SpatialOption { Id = Sonic, Name = "Sonic" } } }; }
            public void SetSpatial(string id, string format)
            {
                Calls.Add("spatial:" + id); Formats[id] = format;
                if (FailSpatialOnce) { FailSpatialOnce = false; throw new Exception("模拟写入后失败"); }
            }
        }
        private static void RunProfileTests()
        {
            var access = new FakeSettings(); access.Volumes["a"] = 0.9F; access.Volumes["b"] = 0.4F; access.Formats["a"] = ""; access.Formats["b"] = "";
            var profiles = new Dictionary<string, DeviceProfile> { { "a", new DeviceProfile { Volume = 30, SpatialFormat = FakeSettings.Sonic } } };
            DeviceProfiles.Apply(0, new[] { "a", "a" }, profiles, access, () => access.Calls.Add("roles"));
            Check(Math.Abs(access.Volumes["a"] - .3F) < .001F && access.Formats["a"] == FakeSettings.Sonic, "target volume and spatial profile applied together");
            Check(access.Volumes["b"] == .4F && access.Calls.Count(c => c == "volume:a") == 1, "profiles affect only selected unique device IDs");
            Check(access.Calls.IndexOf("volume:a") < access.Calls.IndexOf("roles") && access.Calls.IndexOf("spatial:a") < access.Calls.IndexOf("roles"), "settings are applied before routing audio to the target");
            access.Volumes["a"] = .7F;
            DeviceProfiles.Apply(0, new[] { "a" }, profiles, access, () => { });
            Check(Math.Abs(access.Volumes["a"] - .3F) < .001F, "reapplying an already selected device reapplies its volume");
            access.Calls.Clear(); DeviceProfiles.Apply(0, new[] { "b" }, profiles, access, () => access.Calls.Add("roles"));
            Check(access.Calls.SequenceEqual(new[] { "roles" }), "unconfigured device retains existing settings without extra reads");
            access.Volumes["a"] = .7F; access.Formats["a"] = "";
            bool failed = false;
            try { DeviceProfiles.Apply(0, new[] { "a" }, profiles, access, () => { throw new Exception("role failure"); }); } catch { failed = true; }
            Check(failed && access.Volumes["a"] == .7F && access.Formats["a"] == "", "role failure restores original volume and spatial settings");
            access.FailSpatialOnce = true; bool routed = false; failed = false;
            try { DeviceProfiles.Apply(0, new[] { "a" }, profiles, access, () => routed = true); } catch { failed = true; }
            Check(failed && !routed && access.Formats["a"] == "", "partially applied spatial setting rolls back and prevents routing");
            profiles["a"].SpatialFormat = "{11111111-1111-1111-1111-111111111111}"; access.Calls.Clear(); failed = false;
            try { DeviceProfiles.Apply(0, new[] { "a" }, profiles, access, () => routed = true); } catch { failed = true; }
            Check(failed && !routed && !access.Calls.Any(c => c == "volume:a" || c == "spatial:a"), "unsupported preset is rejected before changing settings");
            failed = false; try { DeviceProfiles.Validate(new DeviceProfile { Volume = 101 }, 0); } catch { failed = true; }
            Check(failed, "out-of-range volume rejected");
            failed = false; try { DeviceProfiles.Validate(new DeviceProfile { SpatialFormat = "" }, 1); } catch { failed = true; }
            Check(failed, "capture device cannot be assigned playback spatial effects");
            var legacy = Wire.Decode<Preferences>("{\"AskOnConnect\":false,\"IncludeCommunications\":true}");
            Check(legacy.DeviceProfiles != null && legacy.DeviceProfiles.Count == 0 && !legacy.AskOnConnect, "legacy settings migrate without changing user preferences");
            profiles["a"] = new DeviceProfile { Volume = 0, SpatialFormat = "" };
            var stored = Wire.Decode<Preferences>(Wire.Encode(new Preferences { DeviceProfiles = profiles }));
            Check(stored.DeviceProfiles["a"].Volume == 0 && stored.DeviceProfiles["a"].SpatialFormat == "", "zero volume and disabled spatial effect survive persistence");
            profiles["a"] = new DeviceProfile(); access.Calls.Clear();
            DeviceProfiles.Apply(0, new[] { "a" }, profiles, access, () => access.Calls.Add("roles"));
            Check(access.Calls.SequenceEqual(new[] { "roles" }), "leave-unchanged settings do not overwrite system values");
            Check(SpatialAudio.QuoteArgument("") == "\"\"" && SpatialAudio.QuoteArgument("a\\") == "\"a\\\\\"" && SpatialAudio.QuoteArgument("a\"b") == "\"a\\\"b\"", "helper arguments preserve empty spatial format and escape Windows quoting");
            Check(DeviceProfiles.SameFormat("", Guid.Empty.ToString("B")), "Windows zero GUID is recognized as spatial off");
            string storage = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "profile-storage-test.json"));
            stored.AskOnConnect = false;
            PreferenceStore.Save(storage, stored);
            var loaded = PreferenceStore.Load(storage);
            Check(!loaded.AskOnConnect && loaded.DeviceProfiles["a"].Volume == 0 && loaded.DeviceProfiles["a"].SpatialFormat == "", "per-device settings survive a real file save and reload");
            stored.DeviceProfiles["b"] = new DeviceProfile { Volume = 55, SpatialFormat = FakeSettings.Sonic };
            PreferenceStore.Save(storage, stored); loaded = PreferenceStore.Load(storage);
            Check(loaded.DeviceProfiles.Count == 2 && loaded.DeviceProfiles["b"].Volume == 55, "atomic replacement retains independent device profiles");
        }
        private static void TestSettingsLayout()
        {
            foreach (float scale in new[] { 1F, 1.5F, 2F })
            using (var dialog = new DeviceSettingsDialog(new DeviceSettingsInfo {
                Device = Device("布局验证 · 耳机", 0), CurrentVolume = 62,
                Profile = new DeviceProfile { Volume = 35, Dolby = DolbyProfiles.Capture(new FakeDolby()) },
                Spatial = new SpatialState { Supported = true, Options = new List<SpatialOption> { new SpatialOption { Id = "", Name = "关闭空间音效" } } }
            }, request => Task.FromResult(new Reply())))
            {
                Check(!dialog.IsHandleCreated && !Descendants(dialog).Any(c => (c is NumberBox || c is ScrollCanvas) && c.IsHandleCreated), "layout measurement does not create editor windows during construction");
                dialog.Show(); Application.DoEvents();
                var originalSize = dialog.ClientSize;
                // Exercise proportional layout without changing the user's display settings.
                dialog.AutoScaleMode = AutoScaleMode.None; dialog.Scale(new SizeF(scale, scale));
                Application.DoEvents();
                string suffix = ((int)(scale * 100)).ToString();
                Check(Math.Abs(dialog.ClientSize.Width - originalSize.Width * scale) <= 2, suffix + "% layout simulation actually scales the window");
                var spatial = Descendants(dialog).Single(c => c.Name == "spatial");
                var rule = Descendants(dialog).Single(c => c.Name == "deviceRule");
                var number = Descendants(dialog).Single(c => c.Name == "volumeNumber");
                Check(spatial.Parent == rule.Parent && spatial.Parent == number.Parent && number.Bottom < spatial.Top && spatial.Bottom < rule.Top && rule.Bottom <= rule.Parent.ClientSize.Height,
                    suffix + "% basic settings retain ordered rows inside the page");
                SaveUi(dialog, "settings-layout-" + suffix + ".png");
                var tabs = Descendants(dialog).OfType<SectionTabs>().Single(); tabs.SelectedIndex = 1;
                Application.DoEvents(); SaveUi(dialog, "dolby-layout-" + suffix + ".png");
                var editor = Descendants(dialog).OfType<DolbyEditor>().Single();
                foreach (var field in Descendants(editor).OfType<ComboBox>().Where(c => c.Name == "dolbyPreset" || c.Name == "dolbyIeq" || c.Name.StartsWith("dolbyToggle"))) {
                    var label = Descendants(editor).OfType<Label>().Single(c => c.Name == field.Name + "Label");
                    Check(label.Top == field.Top && label.Height == field.Height && label.TextAlign == ContentAlignment.MiddleLeft,
                        suffix + "% Dolby caption shares its dropdown's vertical center: " + field.Name);
                }
                var visibleField = Descendants(editor).Single(c => c.Name == "dolbyPresetLabel");
                int paints = 0; visibleField.Paint += delegate { paints++; };
                foreach (int movement in new[] { 16, 24, -16 }) {
                    int beforePaints = paints;
                    editor.ScrollBy(movement);
                    Check(paints > beforePaints, suffix + "% scrolling repaints visible fields before the next input or idle message");
                }
                editor.AutoScrollPosition = new Point(0, 10000); Application.DoEvents();
                SaveUi(dialog, "eq-layout-" + suffix + ".png");
                var eq = Descendants(editor).OfType<DolbyEqualizerEditor>().Single();
                var lastNumber = Descendants(eq).Single(c => c.Name == "dolbyDb9");
                Rectangle visibleNumber = editor.RectangleToClient(lastNumber.RectangleToScreen(lastNumber.ClientRectangle));
                Check(editor.ClientRectangle.Contains(visibleNumber), suffix + "% scrolling reaches the final EQ field without clipping");
                var before = Descendants(eq).ToDictionary(c => c, c => c.Bounds);
                var values = eq.Value();
                for (int i = 0; i < 3; i++) {
                    editor.AutoScrollPosition = Point.Empty; tabs.SelectedIndex = 0; tabs.SelectedIndex = 1;
                    editor.ScrollBy(10000); Palette.Apply(true); Palette.Apply(false); Application.DoEvents();
                }
                Check(before.All(pair => pair.Key.Bounds == pair.Value) && values.SequenceEqual(eq.Value()), suffix + "% scrolling, page and theme changes preserve EQ layout and values");
            }
        }
        private static void TestSettingsDialog()
        {
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            var info = new DeviceSettingsInfo { Device = Device("INZONE H5 游戏耳机", 0), CurrentVolume = 62, DeviceRule = DeviceRule.SwitchOnConnect,
                Profile = new DeviceProfile { Volume = 35, SpatialFormat = FakeSettings.Sonic },
                Spatial = new SpatialState { Supported = true, CurrentFormat = FakeSettings.Sonic,
                    Options = new List<SpatialOption> { new SpatialOption { Id = "", Name = "关闭空间音效" }, new SpatialOption { Id = FakeSettings.Sonic, Name = "Windows Sonic（耳机）" } } } };
            Request saved = null;
            using (var dialog = new DeviceSettingsDialog(info, request => { saved = request; return Task.FromResult(new Reply()); }))
            {
                dialog.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, dialog.ClientRectangleWithFrame()); bitmap.Save(Path.Combine(output, "device-settings.png")); }
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "save").PerformClick();
                Check(saved != null && !saved.Value && saved.Profile.Volume == 35 && saved.Profile.SpatialFormat == FakeSettings.Sonic, "device editor saves selected preset without applying");
                Check(saved.DeviceRule == DeviceRule.SwitchOnConnect, "device editor loads and saves auto-connect whitelist with preset");
            }
            using (var dialog = new DeviceSettingsDialog(info, request => { saved = request; return Task.FromResult(new Reply()); }))
            {
                dialog.Show();
                Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "deviceRule").SelectedIndex = 1;
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "apply").PerformClick();
                Check(saved.Value && saved.DeviceId == info.Device.Id, "device editor save-and-apply targets the selected endpoint");
                Check(saved.DeviceRule == DeviceRule.AcceptSystem, "device editor can change whitelist to accept-system mode");
            }
            info.CurrentVolume = null; info.VolumeError = "暂时无法读取"; info.Spatial = null; info.SpatialError = "暂时无法读取";
            using (var dialog = new DeviceSettingsDialog(info, request => { saved = request; return Task.FromResult(new Reply()); }))
            {
                dialog.Show(); Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "deviceRule").SelectedIndex = 1;
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "save").PerformClick();
                Check(saved.Profile.Volume == 35 && saved.Profile.SpatialFormat == FakeSettings.Sonic && saved.DeviceRule == DeviceRule.AcceptSystem,
                    "editing rule while device reads fail preserves saved volume and spatial preset");
            }
            info.Device.Flow = 1; info.Spatial = null; info.Profile.SpatialFormat = null;
            using (var dialog = new DeviceSettingsDialog(info, request => { saved = request; return Task.FromResult(new Reply()); }))
            {
                dialog.Show();
                Check(!Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "spatial").Enabled, "microphone editor disables playback spatial control");
                Check(Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "deviceRule").Enabled, "microphone supports independent whitelist rules");
                Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "deviceRule").SelectedIndex = 0;
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "save").PerformClick();
                Check(saved.DeviceRule == DeviceRule.Normal, "device editor can remove device from whitelist");
            }
        }
        private static void RunWhitelistTests()
        {
            var speaker = Device("speaker", 0); var headset = Device("headset", 0); var other = Device("other", 0);
            var mic = Device("mic", 1); var mic2 = Device("mic2", 1);
            var prefs = new Preferences { DeviceOrder = new List<Endpoint> { speaker, headset, other, mic, mic2 } };
            var initial = State(speaker.Id, mic.Id, speaker, mic);
            var connected = State(headset.Id, mic.Id, speaker, headset, mic);
            prefs.DeviceRules[headset.Id] = DeviceRule.AcceptSystem;
            bool notify, handled;
            var writes = new List<string>();
            var tracker = new ArrivalTracker(initial);
            var errors = DeviceAutomation.Process(tracker, connected, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(handled && !notify && errors.Count == 0 && writes.Count == 0 && tracker.Pending.Count == 0, "accept-system whitelist suppresses arrival before UI and prevents priority override without writes");
            tracker = new ArrivalTracker(State(speaker.Id, mic.Id, speaker, headset, mic));
            errors = DeviceAutomation.Process(tracker, State(headset.Id, mic.Id, headset, mic), prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(!notify && tracker.Pending.Count == 0 && writes.Count == 0, "system fallback to trusted device suppresses disconnect prompt without profile or role writes");
            tracker = new ArrivalTracker(initial);
            tracker.Update(State(speaker.Id, mic.Id, speaker, headset, mic), true);
            Check(tracker.Pending.Count == 1, "late-default test starts with pending arrival");
            DeviceAutomation.Process(tracker, connected, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(handled && tracker.Pending.Count == 0 && writes.Count == 0, "delayed default-only change to trusted device clears existing prompt");
            var stationary = State(speaker.Id, mic.Id, speaker, headset, mic);
            tracker = new ArrivalTracker(initial);
            DeviceAutomation.Process(tracker, stationary, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(notify && tracker.Pending.Count == 1, "trusted device arrival alone does not suppress unless system actually selects it");
            var unchangedTrusted = State(headset.Id, mic.Id, speaker, headset, mic);
            tracker = new ArrivalTracker(unchangedTrusted);
            DeviceAutomation.Process(tracker, State(headset.Id, mic.Id, speaker, headset, other, mic), prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(notify && tracker.Pending.Count == 1, "already-trusted current device does not hide unrelated arrivals");
            Check(DeviceAutomation.StartupTargets(connected, prefs).Count == 0, "startup preserves system-selected trusted endpoint");
            prefs.DeviceRules[headset.Id] = DeviceRule.SwitchOnConnect;
            prefs.UseDevicePriority = false; prefs.AskOnConnect = false;
            writes.Clear(); tracker = new ArrivalTracker(initial);
            DeviceAutomation.Process(tracker, stationary, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(writes.SequenceEqual(new[] { headset.Id }) && !notify && tracker.Pending.Count == 0, "auto-connect whitelist works with priority and connect prompts disabled");
            writes.Clear();
            DeviceAutomation.Process(tracker, stationary, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(writes.Count == 0 && !handled, "duplicate callbacks never reapply auto-connect rule");
            prefs.UseDevicePriority = true; prefs.AskOnConnect = true;
            tracker = new ArrivalTracker(initial); writes.Clear();
            DeviceAutomation.Process(tracker, stationary, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(writes.Single() == headset.Id && !notify && tracker.Pending.Count == 0, "auto-connect overrides higher-priority ordinary device and is silent");
            prefs.DeviceRules[other.Id] = DeviceRule.SwitchOnConnect;
            tracker = new ArrivalTracker(initial); writes.Clear();
            DeviceAutomation.Process(tracker, State(other.Id, mic.Id, speaker, other, headset, mic), prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(writes.SequenceEqual(new[] { headset.Id }) && !notify, "simultaneous auto-connect devices use saved rank regardless of enumeration order");
            prefs.DeviceRules[speaker.Id] = DeviceRule.AcceptSystem;
            tracker = new ArrivalTracker(initial); writes.Clear();
            DeviceAutomation.Process(tracker, stationary, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(writes.Single() == headset.Id, "explicit auto-connect rule precedes passive system acceptance");
            tracker = new ArrivalTracker(initial); writes.Clear();
            DeviceAutomation.Process(tracker, State(headset.Id, mic2.Id, speaker, headset, mic, mic2), prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(notify && tracker.Pending.Count == 1 && tracker.Pending[0].Flow == 1, "silent output does not discard normal microphone prompt");
            prefs.DeviceRules[mic2.Id] = DeviceRule.SwitchOnConnect;
            tracker = new ArrivalTracker(initial); writes.Clear();
            DeviceAutomation.Process(tracker, State(headset.Id, mic2.Id, speaker, headset, mic, mic2), prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(!notify && tracker.Pending.Count == 0 && writes.Contains(headset.Id) && writes.Contains(mic2.Id), "output and microphone whitelists operate independently in one batch");
            tracker = new ArrivalTracker(initial);
            errors = DeviceAutomation.Process(tracker, stationary, prefs, d => { throw new InvalidOperationException("模拟切换失败"); }, out notify, out handled);
            Check(errors.Count == 1 && notify && tracker.Pending.Count == 1, "failed silent switch surfaces error and retains manual recovery prompt");
            prefs.AskOnConnect = false; tracker = new ArrivalTracker(initial);
            errors = DeviceAutomation.Process(tracker, stationary, prefs, d => { throw new InvalidOperationException("模拟切换失败"); }, out notify, out handled);
            Check(errors.Count == 1 && !notify, "silent switch failure still reported when global prompt is disabled");
            prefs.AskOnConnect = true;
            var access = new FakeSettings(); access.Volumes[headset.Id] = .8F; access.Formats[headset.Id] = "";
            prefs.DeviceProfiles[headset.Id] = new DeviceProfile { Volume = 20, SpatialFormat = FakeSettings.Sonic };
            tracker = new ArrivalTracker(initial);
            DeviceAutomation.Process(tracker, stationary, prefs, d => DeviceProfiles.Apply(d.Flow, new[] { d.Id }, prefs.DeviceProfiles, access, () => access.Calls.Add("roles")), out notify, out handled);
            Check(Math.Abs(access.Volumes[headset.Id] - .2F) < .001F && access.Formats[headset.Id] == FakeSettings.Sonic && access.Calls.Last() == "roles" && !notify, "auto-connect applies saved volume and spatial preset before routing silently");
            var legacy = Wire.Decode<Preferences>("{\"UseDevicePriority\":false}");
            Check(legacy.DeviceRules.Count == 0 && DeviceAutomation.Rule(legacy, headset.Id) == DeviceRule.Normal, "legacy preferences start with empty whitelist");
            prefs.DeviceRules[headset.Id] = (DeviceRule)99;
            Check(DeviceAutomation.Rule(prefs, headset.Id) == DeviceRule.Normal, "unknown stored whitelist mode safely uses ordinary behavior");
            prefs.DeviceRules[headset.Id] = DeviceRule.SwitchOnConnect;
            string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "whitelist-storage-test.json"));
            PreferenceStore.Save(path, prefs);
            var loaded = PreferenceStore.Load(path);
            Check(DeviceAutomation.Rule(loaded, speaker.Id) == DeviceRule.AcceptSystem && DeviceAutomation.Rule(loaded, headset.Id) == DeviceRule.SwitchOnConnect && loaded.DeviceProfiles[headset.Id].Volume == 20, "both whitelist modes persist independently alongside existing device presets");
            prefs.DeviceRules.Remove(headset.Id); prefs.DeviceRules.Remove(speaker.Id);
            tracker = new ArrivalTracker(initial); writes.Clear();
            DeviceAutomation.Process(tracker, connected, prefs, d => writes.Add(d.Id), out notify, out handled);
            Check(notify && writes.Single() == speaker.Id, "removing whitelist restores ordinary priority and manual prompt behavior");
        }
        private static void RunWhitelistRegressionTests()
        {
            var speaker = Device("speaker", 0); var razer = Device("Razer Leviathan V2 X", 0);
            var mic = Device("mic", 1);
            const string unavailable = "{1459AC38-3875-49BF-BB59-0FE80F4D395D}";
            var prefs = new Preferences { DeviceOrder = new List<Endpoint> { speaker, razer, mic } };
            prefs.DeviceRules[razer.Id] = DeviceRule.SwitchOnConnect;
            prefs.DeviceProfiles[razer.Id] = new DeviceProfile { Volume = 35, SpatialFormat = unavailable };
            var access = new FakeSettings(); access.Volumes[razer.Id] = .8F; access.Formats[razer.Id] = "";
            var before = State(speaker.Id, mic.Id, speaker, mic);
            var connected = State(speaker.Id, mic.Id, speaker, razer, mic);
            var tracker = new ArrivalTracker(before);
            var warnings = new List<string>(); bool notify, handled;
            Action<Endpoint> select = d => DeviceProfiles.Apply(d.Flow, new[] { d.Id }, prefs.DeviceProfiles, access, () => access.Calls.Add("roles"), message => warnings.Add(message));
            var errors = DeviceAutomation.Process(tracker, connected, prefs, select, out notify, out handled);
            Check(errors.Count == 0 && !notify && tracker.Pending.Count == 0 && access.Calls.Last() == "roles", "Razer unsupported spatial preset does not block quiet whitelist routing or leave an arrival popup");
            Check(Math.Abs(access.Volumes[razer.Id] - .35F) < .001F && access.Formats[razer.Id] == "" && !access.Calls.Any(c => c.StartsWith("spatial:")), "whitelist fallback still applies volume and leaves unavailable spatial format untouched");
            Check(warnings.Count == 1 && prefs.DeviceProfiles[razer.Id].SpatialFormat == unavailable, "skipped whitelist effect reports panel warning without rewriting saved preset");
            access.Calls.Clear(); warnings.Clear();
            DeviceAutomation.Process(tracker, connected, prefs, select, out notify, out handled);
            Check(access.Calls.Count == 0 && !notify, "duplicate callback after spatial fallback does not retry effects or show a popup");
            tracker = new ArrivalTracker(State(speaker.Id, mic.Id, speaker, razer, mic)); warnings.Clear(); access.Calls.Clear();
            errors = DeviceAutomation.Process(tracker, State(razer.Id, mic.Id, razer, mic), prefs, select, out notify, out handled);
            Check(errors.Count == 0 && !notify && tracker.Pending.Count == 0 && access.Calls.Contains("roles"), "priority fallback to an already-online whitelist target suppresses disconnect popup");
            prefs.DeviceRules.Remove(razer.Id);
            tracker = new ArrivalTracker(State(speaker.Id, mic.Id, speaker, razer, mic));
            DeviceAutomation.Process(tracker, State(razer.Id, mic.Id, razer, mic), prefs, d => { }, out notify, out handled);
            Check(notify && tracker.Pending.Single().IsDisconnection, "ordinary disconnect still prompts when fallback target is not whitelisted");
            bool rejected = false;
            try { DeviceProfiles.Apply(0, new[] { razer.Id }, prefs.DeviceProfiles, access, () => { }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "manual preset application still reports unsupported effects instead of claiming full success");
            access.Formats[razer.Id] = unavailable; access.Calls.Clear();
            DeviceProfiles.Apply(0, new[] { razer.Id }, prefs.DeviceProfiles, access, () => access.Calls.Add("roles"));
            Check(access.Calls.Last() == "roles" && !access.Calls.Any(c => c.StartsWith("spatial:")), "already-active saved effect is accepted even when advertised format list omits it");
            access.Formats[razer.Id] = ""; access.Volumes[razer.Id] = .8F;
            prefs.DeviceRules[razer.Id] = DeviceRule.SwitchOnConnect; tracker = new ArrivalTracker(before);
            errors = DeviceAutomation.Process(tracker, connected, prefs,
                d => DeviceProfiles.Apply(d.Flow, new[] { d.Id }, prefs.DeviceProfiles, access, () => { throw new InvalidOperationException("设备切换失败"); }, message => warnings.Add(message)), out notify, out handled);
            Check(errors.Count == 1 && notify && tracker.Pending.Count == 1 && Math.Abs(access.Volumes[razer.Id] - .8F) < .001F, "actual route failure remains visible and rolls volume back even after skipped effect");
            var reply = Wire.Decode<Reply>(Wire.Encode(new Reply { Warning = "已切换，空间音效未应用" }));
            Check(reply.Error == null && reply.Warning != null, "nonblocking preset warning remains separate from action errors over IPC");
        }
        private static void RunPriorityTests()
        {
            var a = Device("a", 0); var b = Device("b", 0); var c = Device("c", 0); var mic = Device("mic", 1); var mic2 = Device("mic2", 1);
            var prefs = new Preferences();
            var initial = State("b", "mic", a, b, mic);
            Check(prefs.UseDevicePriority && Wire.Decode<Preferences>("{\"AskOnConnect\":false}").UseDevicePriority, "priority defaults on for new and legacy preferences");
            Check(DevicePriority.Remember(prefs, initial) && prefs.DeviceOrder.Where(d => d.Flow == 0).First().Id == "b", "initial priority preserves system default at the top");
            Check(!DevicePriority.Remember(prefs, initial), "unchanged discovery avoids repeated preference writes");
            var offline = State("a", "mic", a, mic);
            DevicePriority.Remember(prefs, offline);
            Check(prefs.DeviceOrder.Any(d => d.Id == "b"), "offline endpoint keeps its saved priority");
            Check(DevicePriority.Targets(initial, offline, prefs).Single().Id == "a", "disconnect selects highest available and applies profile even after system fallback");
            Check(DevicePriority.Targets(offline, initial, prefs).Single().Id == "b", "reconnection restores higher-priority endpoint");
            var connected = State("c", "mic", a, b, c, mic);
            DevicePriority.Remember(prefs, connected);
            Check(prefs.DeviceOrder.Where(d => d.Flow == 0).Select(d => d.Id).SequenceEqual(new[] { "b", "a", "c" }), "new devices append without displacing existing priority");
            Check(DevicePriority.Targets(initial, connected, prefs).Single().Id == "b", "lower-priority arrival overrides Windows auto-selection with the preferred device");
            var manual = State("a", "mic", a, b, mic);
            Check(DevicePriority.Targets(initial, manual, prefs).Count == 0, "default-only changes and manual choices are not immediately overridden");
            Check(DevicePriority.Targets(initial, initial, prefs).Count == 0, "self-generated duplicate notifications do not loop");
            DevicePriority.Reorder(prefs, 0, new List<string> { "a", "b" });
            Check(prefs.DeviceOrder.Where(d => d.Flow == 0).Select(d => d.Id).SequenceEqual(new[] { "a", "b", "c" }), "saving stale editor order preserves newly discovered endpoints at the end");
            Check(DevicePriority.Targets(initial, initial, prefs, 0).Single().Id == "a", "saving output order immediately selects output winner only");
            Check(DevicePriority.Targets(initial, initial, prefs, -1).Count == 2, "enabling priority evaluates both output and input");
            Check(DevicePriority.Targets(initial, new AudioState(), prefs).Count == 0, "all endpoints offline produces no invalid switch");
            var withInput = State("a", "mic2", a, b, mic, mic2);
            DevicePriority.Remember(prefs, withInput);
            Check(DevicePriority.Targets(manual, withInput, prefs).Single().Id == "mic", "input topology change does not undo manual output choice");
            DevicePriority.Reorder(prefs, 1, new List<string> { "mic2", "mic" });
            Check(DevicePriority.Targets(initial, initial, prefs, 1).Single().Id == "mic", "offline input preference falls back to online microphone");
            bool rejected = false;
            try { DevicePriority.Reorder(prefs, 0, new List<string> { "a", "a" }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "duplicate priority IDs are rejected");
            rejected = false;
            try { DevicePriority.Reorder(prefs, 0, new List<string> { "mic" }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "input IDs cannot be inserted into output order");
            prefs.UseDevicePriority = false;
            Check(DevicePriority.Targets(initial, connected, prefs, -1, true).Count == 0, "disabled priority never auto-selects, including startup and explicit order saves");
            string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "priority-storage-test.json"));
            PreferenceStore.Save(path, prefs);
            var loaded = PreferenceStore.Load(path);
            Check(!loaded.UseDevicePriority && loaded.DeviceOrder.Select(d => d.Id).SequenceEqual(prefs.DeviceOrder.Select(d => d.Id)), "priority order and explicit disabled preference persist across reload");
            loaded.UseDevicePriority = true; loaded.IncludeCommunications = false;
            var split = State("a", "mic", a, b, mic); split.Defaults["0:2"] = "b";
            Check(DevicePriority.Targets(split, split, loaded, null, true).Count == 0, "startup respects excluded communications role");
            loaded.IncludeCommunications = true;
            Check(DevicePriority.Targets(split, split, loaded, null, true).Single().Id == "a", "startup reconciles saved priority with included communications role");
            var access = new FakeSettings(); access.Volumes["a"] = .8F; access.Formats["a"] = "";
            loaded.DeviceProfiles["a"] = new DeviceProfile { Volume = 25, SpatialFormat = FakeSettings.Sonic };
            foreach (var target in DevicePriority.Targets(initial, offline, loaded))
                DeviceProfiles.Apply(target.Flow, new[] { target.Id }, loaded.DeviceProfiles, access, () => access.Calls.Add("roles"));
            Check(Math.Abs(access.Volumes["a"] - .25F) < .001F && access.Formats["a"] == FakeSettings.Sonic && access.Calls.Last() == "roles", "automatic fallback uses the same volume and spatial preset application path");
        }
        private static void TestPriorityDialog()
        {
            var a = Device("桌面扬声器", 0); var b = Device("蓝牙耳机", 0); var c = Device("USB 耳机", 0); var mic = Device("麦克风", 1);
            var prefs = new Preferences { DeviceOrder = new List<Endpoint> { b, a, c, mic } };
            var state = State(a.Id, mic.Id, a, c, mic);
            var reply = new Reply { State = state, Preferences = prefs, Pending = new List<Arrival>() };
            Request captured = null;
            using (var dialog = new DevicePriorityDialog(0, reply, request => { captured = request; return Task.FromResult(new Reply()); }))
            {
                dialog.Show(); Application.DoEvents();
                var list = Descendants(dialog).OfType<ListBox>().Single();
                Check(list.Items.Count == 3 && !Descendants(dialog).OfType<Button>().Single(bu => bu.Name == "moveUp").Enabled, "priority editor includes offline devices and protects first-row boundary");
                list.SelectedIndex = 2;
                Descendants(dialog).OfType<Button>().Single(bu => bu.Name == "moveUp").PerformClick();
                Check(list.SelectedIndex == 1, "move-up keeps moved device selected");
                string imagePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "device-priority.png"));
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, dialog.ClientRectangleWithFrame()); bitmap.Save(imagePath); }
                Descendants(dialog).OfType<Button>().Single(bu => bu.Name == "saveOrder").PerformClick();
                Check(captured != null && captured.Action == "deviceOrder" && captured.Flow == 0 && captured.DeviceOrder.SequenceEqual(new[] { b.Id, c.Id, a.Id }), "priority editor submits reviewed device order and correct flow");
                Check(prefs.DeviceOrder[1].Id == a.Id, "editing order does not mutate unsaved shared preferences");
            }
            using (var dialog = new DevicePriorityDialog(1, reply, request => Task.FromResult(new Reply { Error = "模拟保存失败" })))
            {
                dialog.Show();
                Descendants(dialog).OfType<Button>().Single(bu => bu.Name == "saveOrder").PerformClick();
                Check(!dialog.IsDisposed && Descendants(dialog).OfType<Label>().Any(l => l.Text == "模拟保存失败"), "priority editor remains open with actionable save error");
                dialog.Close();
            }
            using (var dashboard = new Dashboard(false, true))
            {
                dashboard.Show(); dashboard.RenderReply(reply);
                Check(Descendants(dashboard).OfType<CheckBox>().Single(ch => ch.Name == "usePriority").Checked, "dashboard shows enabled priority option by default");
                Check(Descendants(dashboard).OfType<Button>().Count(bu => bu.Name.StartsWith("order")) == 2, "dashboard exposes separate output and input ordering controls");
                Check(Descendants(dashboard).OfType<Button>().Any(bu => bu.Name == "exportSettings" && bu.Visible) && Descendants(dashboard).OfType<Button>().Any(bu => bu.Name == "importSettings" && bu.Visible), "dashboard exposes import and export actions");
                prefs.UseDevicePriority = false; dashboard.RenderReply(reply);
                Check(!Descendants(dashboard).OfType<CheckBox>().Single(ch => ch.Name == "usePriority").Checked, "dashboard reflects persisted system-selection mode");
                reply.Warning = "已切换，当前空间音效不支持"; dashboard.RenderReply(reply);
                Check(Descendants(dashboard).OfType<Label>().Any(l => l.Visible && l.Text == reply.Warning), "dashboard displays nonblocking spatial warning without a separate popup");
                reply.Warning = null; dashboard.RenderReply(reply);
                Check(!Descendants(dashboard).OfType<Label>().Any(l => l.Visible && l.Text == "已切换，当前空间音效不支持"), "cleared warning disappears even when device state is unchanged");
                dashboard.Close();
            }
            prefs.UseDevicePriority = true;
            var tracker = new ArrivalTracker(State(a.Id, mic.Id, a, mic)); tracker.Update(state, true);
            reply.Pending = tracker.Pending;
            using (var prompt = new DevicePrompt(request => Task.FromResult(reply), true, reply))
            {
                prompt.Show(); Application.DoEvents();
                var picker = Descendants(prompt).OfType<ComboBox>().Single();
                Check(((Endpoint)picker.Items[0]).Id == a.Id && picker.Items.Count == 2, "priority popup uses saved order and omits offline targets");
                Check(prompt.Width <= 365 && prompt.Height <= 220, "priority status keeps popup compact");
                prompt.Close();
            }
        }
        private static void RunConfigurationTests()
        {
            var speaker = Device("speaker", 0); speaker.Name = "扬声器 \"主力\" \\ 测试";
            var offline = Device("offline-headset", 0); var mic = Device("mic", 1);
            var current = new Preferences { DarkMode = true, AskOnConnect = false, IncludeCommunications = false, UseDevicePriority = false,
                DeviceOrder = new List<Endpoint> { speaker, offline, mic } };
            current.DeviceProfiles[speaker.Id] = new DeviceProfile { Volume = 0, SpatialFormat = "" };
            current.DeviceProfiles[offline.Id] = new DeviceProfile { Volume = null, SpatialFormat = FakeSettings.Sonic };
            current.DeviceRules[speaker.Id] = DeviceRule.AcceptSystem; current.DeviceRules[offline.Id] = DeviceRule.SwitchOnConnect;
            string exported = PreferenceStore.Export(current);
            var document = Wire.Decode<ConfigurationFile>(exported);
            Check(document.Format == "AudioSwitch.Settings" && document.Version == 1 && exported.Contains(Environment.NewLine), "runtime and backup use one versioned readable JSON format");
            Check(Wire.Encode(PreferenceStore.Parse(exported)) == Wire.Encode(current), "configuration round trip preserves all flags, rank, offline devices, presets, whitelist, Unicode and escaped names");
            Check(document.Settings.DarkMode && Wire.Decode<Preferences>(Wire.Encode(current)).DarkMode, "dark mode survives backup and IPC serialization");
            var legacy = PreferenceStore.Parse("{\"AskOnConnect\":false,\"IncludeCommunications\":true}");
            Check(!legacy.AskOnConnect && legacy.IncludeCommunications && legacy.UseDevicePriority && legacy.DeviceRules.Count == 0, "legacy import retains saved values and fills new defaults");
            Check(!legacy.DarkMode && !new Preferences().DarkMode, "old configurations and new installations default to light mode");
            var invalid = new[] { "", "{}", "[]", "{broken", "{\"hello\":true}", exported.Replace("\"Version\": 1", "\"Version\": 99"),
                exported.Replace("AudioSwitch.Settings", "AnotherApp"), "{\"AskOnConnect\":\"false\"}", "{\"DarkMode\":\"true\"}", "{\"DarkMode\":1}", "{\"DarkMode\":null}", "{\"DeviceRules\":{\"a\":99}}",
                "{\"DeviceProfiles\":{\"a\":{\"Volume\":101}}}", "{\"DeviceProfiles\":{\"a\":{\"Volume\":5.5}}}",
                "{\"DeviceProfiles\":{\"a\":{\"SpatialFormat\":\"invalid\"}}}", "{\"DeviceOrder\":[{\"Id\":\"a\",\"Name\":\"A\",\"Flow\":2}]}",
                "{\"DeviceOrder\":[{\"Id\":\"a\",\"Name\":\"A\",\"Flow\":0},{\"Id\":\"a\",\"Name\":\"B\",\"Flow\":0}]}",
                "{\"DeviceProfiles\":{\"mic\":{\"SpatialFormat\":\"\"}},\"DeviceOrder\":[{\"Id\":\"mic\",\"Name\":\"Mic\",\"Flow\":1}]}" };
            foreach (var json in invalid)
            {
                bool rejected = false; try { PreferenceStore.Parse(json); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "invalid or incompatible configuration is rejected: " + json.Substring(0, Math.Min(45, json.Length)));
            }
            string folder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "configuration-tests"));
            string path = Path.Combine(folder, "settings.json");
            PreferenceStore.Save(path, current);
            Check(File.ReadAllText(path) == exported && Wire.Encode(PreferenceStore.Load(path)) == Wire.Encode(current), "active settings use exactly the exported backup format");
            var imported = new Preferences { DeviceOrder = new List<Endpoint> { offline, speaker, mic } };
            imported.DeviceRules[mic.Id] = DeviceRule.SwitchOnConnect;
            string backup;
            var result = PreferenceStore.Import(path, PreferenceStore.Export(imported), current, out backup);
            Check(File.Exists(backup) && Wire.Encode(PreferenceStore.Load(backup)) == Wire.Encode(current), "import writes a restorable complete backup before replacement");
            Check(Wire.Encode(result) == Wire.Encode(imported) && Wire.Encode(PreferenceStore.Load(path)) == Wire.Encode(imported), "import replaces the full configuration including removal of old profiles and rules");
            string before = File.ReadAllText(path); int backupCount = Directory.GetFiles(Path.Combine(folder, "backups")).Length;
            bool failed = false;
            try { PreferenceStore.Import(path, "{\"DeviceRules\":{\"bad\":10}}", imported, out backup); } catch (InvalidOperationException) { failed = true; }
            Check(failed && File.ReadAllText(path) == before && Directory.GetFiles(Path.Combine(folder, "backups")).Length == backupCount, "failed validation cannot change active config or create a misleading backup");
            string migration = Path.Combine(folder, "legacy.json"); File.WriteAllText(migration, Wire.Encode(current));
            var migrated = PreferenceStore.Load(migration);
            Check(Wire.Encode(migrated) == Wire.Encode(current) && File.ReadAllText(migration).Contains("AudioSwitch.Settings"), "old active settings automatically migrate without preference loss");
            string corrupt = Path.Combine(folder, "corrupt.json"); File.WriteAllText(corrupt, "{bad");
            failed = false; try { PreferenceStore.Load(corrupt); } catch (InvalidOperationException) { failed = true; }
            Check(failed && File.ReadAllText(corrupt) == "{bad", "damaged active config is preserved instead of silently reset");
            failed = false; try { PreferenceStore.Parse(new string(' ', PreferenceStore.MaxBytes + 1)); } catch (InvalidOperationException) { failed = true; }
            Check(failed, "oversized backup is rejected");
            string blocked = Path.Combine(folder, "blocked"); Directory.CreateDirectory(blocked);
            string blockedPath = Path.Combine(blocked, "settings.json"); PreferenceStore.Save(blockedPath, current);
            File.WriteAllText(Path.Combine(blocked, "backups"), "a file prevents backup creation");
            failed = false; try { PreferenceStore.Import(blockedPath, exported, current, out backup); } catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(blockedPath) == exported, "backup write failure aborts import without touching active config");
        }
        private static void TestDismissibleNotice()
        {
            var speaker = Device("扬声器 (Razer Leviathan V2 X)", 0);
            var reply = new Reply { State = State(speaker.Id, null, speaker), Pending = new List<Arrival>(), Preferences = new Preferences(),
                Warning = "扬声器 (Razer Leviathan V2 X) 已切换；暂不支持保存的空间音效，本次保留设备当前音效。可在设备设置中修改。" };
            Request captured = null;
            using (var window = new Dashboard(false, true, request => { captured = request; return Task.FromResult(new Reply()); }))
            {
                window.Show(); window.RenderReply(reply); Application.DoEvents();
                var banner = Descendants(window).OfType<Panel>().Single(p => p.Name == "panelNotice");
                var close = Descendants(window).OfType<Button>().Single(b => b.Name == "dismissNotice");
                var label = Descendants(window).OfType<Label>().Single(l => l.Name == "noticeText");
                Check(banner.Visible && close.Visible && !close.Bounds.IntersectsWith(label.Bounds), "panel notice close button is visible and does not overlap message");
                string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "dismissible-notice.png"));
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, window.ClientRectangleWithFrame()); bitmap.Save(path); }
                close.PerformClick();
                Check(!banner.Visible && captured.Action == "dismissWarning" && captured.Message == reply.Warning, "close immediately hides panel warning and acknowledges exact message to backend");
                window.RenderReply(reply);
                Check(!banner.Visible, "unchanged warning stays dismissed during background refresh");
                reply.Error = "设备切换失败"; window.RenderReply(reply);
                Check(banner.Visible && label.Text == reply.Error, "new error remains visible after warning was dismissed");
                var details = Descendants(banner).OfType<Button>().Single(b => b.Name == "noticeDetails");
                Check(Descendants(banner).OfType<Label>().Single(l => l.Name == "noticeHeading").Text == "操作未完成" && !details.Bounds.IntersectsWith(label.Bounds), "inline failure card has a heading and a separate details action");
                SaveUi(window, "inline-error-light.png");
                reply.Preferences.DarkMode = true; window.RenderReply(reply); SaveUi(window, "inline-error-dark.png");
                Check(banner.BackColor == Palette.Background && label.Text == reply.Error, "theme change keeps the inline error on a neutral card without losing the message");
                reply.Preferences.DarkMode = false; window.RenderReply(reply);
                close.PerformClick(); window.RenderReply(reply);
                Check(!banner.Visible, "closing error keeps same error and already-dismissed warning hidden");
                reply.Warning = "另一设备的音效尚未应用"; window.RenderReply(reply);
                Check(banner.Visible && label.Text == reply.Warning, "new warning is shown without reviving dismissed error");
                close.PerformClick(); window.RenderReply(reply);
                Check(!banner.Visible, "closing new warning does not bring back dismissed error on refresh");
                reply.Error = null; reply.Warning = null; window.RenderReply(reply);
                reply.Warning = "另一设备的音效尚未应用"; window.RenderReply(reply);
                Check(banner.Visible, "a warning can reappear after previous condition cleared and recurred");
                close.PerformClick(); window.Close();
            }
            reply.Warning = null;
            using (var reopened = new Dashboard(false, true))
            {
                reopened.Show(); reopened.RenderReply(reply);
                Check(!Descendants(reopened).OfType<Panel>().Single(p => p.Name == "panelNotice").Visible, "reopened panel has no warning after backend acknowledgement");
                reopened.Close();
            }
        }
        private static void SettingsSmoke()
        {
            using (var audio = new AudioService())
            {
                var before = audio.Read();
                var device = before.Devices.First(d => d.Flow == 0 && !before.Defaults.Values.Contains(d.Id));
                float volume = audio.ReadVolume(device.Id); string spatial = audio.ReadSpatial(device.Id).CurrentFormat;
                audio.SetVolume(device.Id, volume);
                audio.SetSpatial(device.Id, spatial);
                Check(Math.Abs(audio.ReadVolume(device.Id) - volume) < .011F && DeviceProfiles.SameFormat(audio.ReadSpatial(device.Id).CurrentFormat, spatial), "native volume setter and spatial helper verify unchanged settings on non-default endpoint");
                var after = audio.Read();
                Check(before.Defaults.All(p => after.Defaults.ContainsKey(p.Key) && after.Defaults[p.Key] == p.Value), "settings smoke check leaves all default device roles unchanged");
            }
        }
    }
}
