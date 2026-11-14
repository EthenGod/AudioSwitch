// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;

namespace AudioSwitch
{
    internal static class AppearancePreferenceTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var old = PreferenceStore.Parse("{\"DarkMode\":false}");
            check(old.UiStyle == "classic" && !old.DarkMode, "old config defaults to classic without changing light mode");
            check(new Preferences().UiStyle == "classic" && new Preferences().DarkMode, "new config starts with classic dark appearance");
            check(!PreferenceStore.Export(old).Contains("UiStyle"), "default style is omitted for older config readers");
            var future = new Preferences { UiStyle = "future-style-2", DarkMode = false };
            string json = PreferenceStore.Export(future);
            check(PreferenceStore.Parse(json).UiStyle == future.UiStyle && !PreferenceStore.Parse(json).DarkMode,
                "export preserves future style IDs independently from color mode");
            check(Wire.Decode<Preferences>(Wire.Encode(future)).UiStyle == future.UiStyle, "IPC retains the style ID");
            check(PreferenceStore.Parse("{\"UiStyle\":\"" + new String('a', 32) + "\"}").UiStyle.Length == 32, "style ID accepts exact length limit");
            foreach (string invalid in new[] { "null", "true", "42", "[]", "{}", "\"\"", "\"Classic\"", "\"-style\"", "\"a b\"", "\"../style\"", "\"a\\n\"", "\"深色\"", "\"" + new String('a', 33) + "\"" })
            {
                bool rejected = false;
                try { PreferenceStore.Parse("{\"UiStyle\":" + invalid + "}"); } catch (InvalidOperationException) { rejected = true; }
                check(rejected, "invalid style value rejected: " + invalid);
            }
            string path = Path.Combine(Path.GetTempPath(), "AudioSwitch-style-" + Guid.NewGuid().ToString("N"), "settings.json");
            PreferenceStore.Save(path, future);
            check(PreferenceStore.Load(path).UiStyle == future.UiStyle, "saved style survives a fresh configuration load");
            string before = File.ReadAllText(path);
            bool saveRejected = false;
            try { PreferenceStore.Save(path, new Preferences { UiStyle = null }); } catch (InvalidOperationException) { saveRejected = true; }
            check(saveRejected && File.ReadAllText(path) == before, "invalid style cannot replace the saved configuration");
            string backup;
            var imported = PreferenceStore.Import(path, PreferenceStore.Export(old), future, out backup);
            check(imported.UiStyle == "classic" && !imported.DarkMode && PreferenceStore.Load(path).UiStyle == "classic"
                && PreferenceStore.Load(backup).UiStyle == "future-style-2", "import restores old appearance and backs up the previous style");
        }
    }
}
