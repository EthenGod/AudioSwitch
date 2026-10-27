using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace AudioSwitch
{
    internal static class FrontendLaunchTests
    {
        private sealed class Window : IBrowserFrontend
        {
            public int Id { get; set; }
            public bool Exited { get; set; }
            internal int Activations, Closes, Disposals;
            internal bool Persistent;
            public void KeepOpen(bool value) { Persistent = value; }
            public void Activate() { Activations++; }
            public void Close() { Closes++; }
            public void Dispose() { Disposals++; }
        }
        internal static void Run(Action<bool, string> check)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frontend-launch-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var old = PanelLaunch.Select(Path.Combine(root, "AudioSwitch.exe"), false, 123);
            check(old.Arguments.Contains("--ui") && old.FileName.EndsWith("AudioSwitch.exe") && !old.UseShellExecute, "old eight-file layout retains WinForms launch without shell");
            check(PanelLaunch.Select(Path.Combine(root, "AudioSwitch.exe"), true, 123).Arguments == "--prompt --owner=123", "legacy prompt preserves owner");
            Reject(() => PanelLaunch.Select(Path.Combine(root, "AudioSwitch.exe"), false, 0), check, "invalid owner rejected");
            Reject(() => PanelLaunch.Select(Path.Combine(root, "AudioSwitch.exe"), false, 123, true), check, "browser never silently opens legacy or preview UI");
            check(!PreferenceStore.Parse("{\"AskOnConnect\":true}").OpenPanelInBrowser, "old configuration defaults to native tray double click");
            check(PreferenceStore.Parse(PreferenceStore.Export(new Preferences { OpenPanelInBrowser = true })).OpenPanelInBrowser, "browser tray preference survives export and import");
            string settingsPath = Path.Combine(root, "settings.json");
            PreferenceStore.Save(settingsPath, new Preferences { OpenPanelInBrowser = true });
            check(PreferenceStore.Load(settingsPath).OpenPanelInBrowser, "browser tray preference survives settings reload");
            Reject(() => PreferenceStore.Parse("{\"OpenPanelInBrowser\":\"true\"}"), check, "browser tray preference requires boolean");
            foreach (string mode in new[] { "valid", "format", "version", "platform", "count", "duplicate", "traversal", "length", "hash", "sizeType", "missing", "corrupt", "panelOnly", "manifestOnly" }) {
                string dir = Path.Combine(root, mode); Directory.CreateDirectory(dir);
                var rows = new List<Dictionary<string, object>>();
                foreach (string name in PanelLaunch.Files) {
                    string path = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture:" + name);
                    string digest;
                    using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                    rows.Add(new Dictionary<string, object> { { "Path", name }, { "Length", new FileInfo(path).Length }, { "Sha256", digest } });
                }
                var manifest = new Dictionary<string, object> { { "Format", "AudioSwitch.UI.Candidate" }, { "FormatVersion", 1 }, { "AppVersion", AppVersion.Number }, { "Platform", "win-x64" }, { "Files", rows } };
                if (mode == "format") manifest["FormatVersion"] = "1";
                if (mode == "version") manifest["AppVersion"] = "99.0.0";
                if (mode == "platform") manifest["Platform"] = "win-arm64";
                if (mode == "count") rows.RemoveAt(0);
                if (mode == "duplicate") rows[1]["Path"] = rows[0]["Path"];
                if (mode == "traversal") rows[0]["Path"] = "../AudioSwitch.exe";
                if (mode == "length") rows[0]["Length"] = 999;
                if (mode == "hash") rows[0]["Sha256"] = new string('0',64);
                if (mode == "sizeType") rows[0]["Length"] = rows[0]["Length"].ToString();
                if (mode == "missing" || mode == "manifestOnly") File.Move(Path.Combine(dir, "audio-switch-panel.exe"), Path.Combine(dir, "panel.saved"));
                if (mode == "corrupt") File.AppendAllText(Path.Combine(dir, "LICENSE"), "broken");
                if (mode != "panelOnly") File.WriteAllText(Path.Combine(dir, "candidate-manifest.json"), Wire.Encode(manifest));
                bool binaryChecked = false;
                if (mode == "valid") {
                    PanelLaunch.Validate(dir, AppVersion.Number, (path, version) => binaryChecked = Path.GetFileName(path) == "audio-switch-panel.exe" && version == AppVersion.Number);
                    check(binaryChecked, "valid candidate reaches native executable verification after all hashes");
                    Reject(() => PanelLaunch.Select(Path.Combine(dir,"AudioSwitch.exe"),false,123),check,"fake native executable rejected despite matching manifest");
                } else {
                    Reject(() => PanelLaunch.Validate(dir, AppVersion.Number, (path, version) => binaryChecked = true), check, "reject candidate " + mode);
                    check(!binaryChecked, "candidate " + mode + " rejected before executable use");
                }
            }
            var created = new List<Window>(); bool fail = false;
            using (var windows = new FrontendWindows(p => { if (fail) throw new InvalidDataException("bad candidate"); return new ProcessStartInfo(p ? "prompt" : "panel"); }, info => {
                var window = new Window { Id = 100 + created.Count }; created.Add(window); return window;
            })) {
                windows.Show(false); int first = windows.PanelPid;
                windows.Show(false); windows.Show(false);
                check(created.Count == 1 && windows.PanelPid == first && created[0].Activations == 2, "repeated panel open reuses and activates one process");
                windows.Show(true); windows.Show(true);
                check(created.Count == 2 && windows.PromptPid != first && created[1].Activations == 0, "prompt separate and repeat notification never steals focus");
                created[0].Exited = true; windows.Show(false);
                check(created.Count == 3 && created[0].Disposals == 1 && windows.PanelPid != first, "closed panel recreated and old handle released");
                created[2].Exited = true; fail = true;
                Reject(() => windows.Show(false), check, "invalid candidate cannot launch on reopen");
                check(created.Count == 3 && windows.PanelPid == 0, "failed launch not reported as an active window");
                fail = false; windows.Show(false); windows.Close(); windows.Close(); windows.Show(false);
                check(created.Count == 4 && created[1].Closes == 1 && created[3].Closes == 1, "shutdown closes owned windows once and blocks late launch");
            }
            check(created.All(w => w.Disposals == 1), "all owned process handles disposed exactly once");
            created.Clear();
            using (var windows = new FrontendWindows(p => new ProcessStartInfo(p ? "prompt" : "panel"), info => {
                var window = new Window { Id = 200 + created.Count }; created.Add(window); return window;
            }, () => new ProcessStartInfo("browser"))) {
                windows.ShowBrowser(); windows.ShowBrowser(); windows.Show(false);
                check(created.Count == 2 && created[0].Activations == 1, "browser host is reused independently of native panel");
                created[0].Exited = true; windows.ShowBrowser();
                check(created.Count == 3 && created[0].Disposals == 1, "browser host recreates after last tab closes");
                windows.Close(); windows.ShowBrowser();
                check(created.Count == 3 && created[1].Closes == 1 && created[2].Closes == 1, "backend exit closes both panel transports and blocks reopen");
            }
            check(created.All(w => w.Disposals == 1), "browser handles disposed exactly once");
            ProcessStartInfo launch = null; Window retained = null;
            using (var windows = new FrontendWindows(p => new ProcessStartInfo("panel"), info => { launch = info; return retained = new Window { Id = 300 }; }, () => new ProcessStartInfo("browser", "--browser"))) {
                windows.SetBrowserPersistent(true);
                check(launch.Arguments.Contains("--keep-browser") && launch.Arguments.Contains("--browser-idle") && retained.Activations == 0, "persistent port starts without opening a browser window");
                windows.ShowBrowser(); check(retained.Activations == 1, "opening retained browser uses existing host");
                windows.SetBrowserPersistent(true); check(retained.Persistent, "existing browser host receives keep command");
                windows.SetBrowserPersistent(false); check(!retained.Persistent && retained.Closes == 0, "disabling persistence releases idle lifetime without interrupting open tabs");
                windows.Close(); check(retained.Closes == 1, "backend exit closes persistent port");
            }
            check(!PreferenceStore.Parse("{\"AskOnConnect\":true}").KeepBrowserPortOpen, "old configuration never keeps a port open");
            PreferenceStore.Save(settingsPath, new Preferences { KeepBrowserPortOpen = true });
            check(PreferenceStore.Load(settingsPath).KeepBrowserPortOpen && PreferenceStore.Parse(PreferenceStore.Export(PreferenceStore.Load(settingsPath))).KeepBrowserPortOpen, "persistent port preference survives save and backup roundtrip");
            Reject(() => PreferenceStore.Parse("{\"KeepBrowserPortOpen\":1}"), check, "persistent port preference rejects non-boolean values");
        }
        private static void Reject(Action action, Action<bool,string> check, string name) { bool rejected = false; try { action(); } catch { rejected = true; } check(rejected,name); }
    }
}
