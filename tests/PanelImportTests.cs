// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;

namespace AudioSwitch
{
    internal static class PanelImportTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var current = new Preferences(); var state = new AudioState();
            state.Devices.Add(new Endpoint { Id = "online", Name = "同名设备", Flow = 0 });
            state.Defaults["0:1"] = "online";
            var incoming = new Preferences { DarkMode = true };
            incoming.DeviceOrder.Add(new Endpoint { Id = "online", Name = "同名设备", Flow = 0 });
            incoming.DeviceOrder.Add(new Endpoint { Id = "offline", Name = "同名设备", Flow = 0 });
            incoming.DeviceProfiles["offline"] = new DeviceProfile { Volume = null, SpatialFormat = "", Dolby = new DolbyProfile { Enabled = true, MainProfile = 4, SubProfile = 4, Eq = Enumerable.Range(0, 20).ToArray() } };
            incoming.DeviceRules["offline"] = DeviceRule.AcceptSystem;
            var original = Wire.Encode(current); var originalState = Wire.Encode(state);
            var preview = PanelImport.Preview(PreferenceStore.Export(incoming), current, state);
            check(Wire.Encode(current) == original && Wire.Encode(state) == originalState, "import preview leaves configuration and audio snapshot unchanged");
            check(preview.ImportPreview.Devices == 2 && preview.ImportPreview.OfflineDevices == 1 && preview.ImportPreview.Profiles == 1 && preview.ImportPreview.Rules == 1 && preview.ImportPreview.DolbyProfiles == 1, "import preview counts by endpoint ID including offline and Dolby");
            check(Wire.Encode(PreferenceStore.Parse(preview.ConfigurationJson)) == Wire.Encode(incoming), "import preview retains null, off, and complete 20-point Dolby data");
            PanelImport.RequireUnchanged(current, preview.ConfigurationRevision);
            check(preview.ConfigurationRevision == PanelImport.Revision(PreferenceStore.Parse(PreferenceStore.Export(current))), "configuration revision survives serialization");
            current.GameMode = true;
            Reject(delegate { PanelImport.RequireUnchanged(current, preview.ConfigurationRevision); }, check, "import rejects changes after preview");
            Reject(delegate { PanelImport.RequireUnchanged(current, null); }, check, "import requires preview revision");
            var rev = PanelImport.Revision(incoming); incoming.DeviceProfiles["offline"].Dolby.Eq[19]++;
            check(PanelImport.Revision(incoming) != rev, "import conflict includes Dolby-only edits");
            Reject(delegate { PanelImport.Preview("{invalid", current, state); }, check, "import rejects malformed JSON before confirmation");
            Reject(delegate { PanelImport.Preview(new string('x', PreferenceStore.MaxBytes + 1), current, state); }, check, "import rejects oversized configuration");
            Reject(delegate { PanelImport.Preview(Wire.Encode(new ConfigurationFile { Format = "AudioSwitch.Settings", Version = 1, Settings = incoming }).Replace("\"DarkMode\":true", "\"DarkMode\":1"), current, state); }, check, "import uses strict existing type validation");
            // Only a fresh temporary configuration; never touch the active SettingsPath.
            var folder = Path.Combine(Path.GetTempPath(), "AudioSwitch-panel-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); var path = Path.Combine(folder, "settings.json");
            PreferenceStore.Save(path, current); var before = File.ReadAllText(path);
            var prepared = PanelImport.Preview(PreferenceStore.Export(incoming), current, state);
            PanelImport.RequireUnchanged(current, prepared.ConfigurationRevision);
            string backup; var result = PreferenceStore.Import(path, prepared.ConfigurationJson, current, out backup);
            check(Wire.Encode(PreferenceStore.Parse(File.ReadAllText(backup))) == Wire.Encode(PreferenceStore.Parse(before)), "confirmed import keeps complete recoverable backup");
            check(Wire.Encode(result) == Wire.Encode(incoming) && Wire.Encode(state) == originalState, "confirmed configuration import does not alter audio state");
        }
        private static void Reject(Action action, Action<bool, string> check, string description)
        {
            bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, description);
        }
    }
}
