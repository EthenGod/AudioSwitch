// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class LocalizationTests
    {
        internal static void Run(Action<bool, string> check)
        {
            check(L.Match(new[] { "ja-JP" }) == "ja" && L.Match(new[] { "zz-ZZ", "en-GB" }) == "en", "language matching respects system order and regional tags");
            check(L.Match(new[] { "zz-ZZ" }) == "zh-CN" && L.Resolve("unknown") == "zh-CN", "unsupported language falls back to Chinese");
            check(new Preferences().Language == null && PreferenceStore.Parse("{\"DarkMode\":true}").Language == null, "new and old configurations ask for language once");
            check(!PreferenceStore.Export(new Preferences()).Contains("Language"), "unconfirmed recommendation is not saved as a choice");
            check(L.Resolve(L.SystemChoice, "ja") == "ja" && L.Resolve(L.SystemChoice, "en") == "en" && L.Resolve(L.SystemChoice, "unknown") == "zh-CN", "system choice follows supported system languages and falls back to Chinese");
            check(L.Resolve("en", "ja") == "en" && L.Resolve("fr-FR", "ja") == "zh-CN", "explicit choices do not follow later system changes");
            check(L.SupportedChoice(L.SystemChoice) && !L.SupportedChoice("../en") && !L.SupportedChoice("fr-FR"), "language commands distinguish system mode from unsupported catalogues");
            foreach (string id in new[] { "zh-CN", "en", "ja", "fr-FR", L.SystemChoice })
            {
                var preferences = new Preferences { Language = id, DarkMode = false };
                check(PreferenceStore.Parse(PreferenceStore.Export(preferences)).Language == id && !preferences.DarkMode, "language survives backup independently from appearance: " + id);
                check(Wire.Decode<Preferences>(Wire.Encode(preferences)).Language == id, "language survives IPC: " + id);
            }
            foreach (string invalid in new[] { "true", "42", "[]", "{}", "\"\"", "\"../en\"", "\"en\\n\"", "\"中文\"" })
            {
                bool rejected = false;
                try { PreferenceStore.Parse("{\"Language\":" + invalid + "}"); } catch (InvalidOperationException) { rejected = true; }
                check(rejected, "invalid language preference rejected: " + invalid);
            }
            var command = Wire.Decode<Request>(Wire.Encode(new Request { Action = "language", Language = "ja" }));
            check(command.Action == "language" && command.Language == "ja" && command.DeviceId == null, "language request has no device operation");
            string before = L.Language;
            try
            {
                L.SetLanguage("en");
                check(L.Text("关闭") == "Close" && L.Text("关闭开关") == "Off", "window close and setting off have distinct translations");
                check(L.Text("缺失：{0}", "Device {1}") == "Missing: Device {1}", "format arguments are inserted once without interpreting user text");
                var result = new IntegrityResult();
                result.Pass(L.Text("通过：{0}", "kept.bin"), "kept.bin"); result.Fail(L.Text("缺失：{0}", "lost.bin"), "lost.bin");
                check(result.PassedFiles.SequenceEqual(new[] { "kept.bin" }) && result.EntryStatuses.SequenceEqual(new[] { "passed", "failed" }), "integrity and repair decisions are independent from translated messages");
                L.SetLanguage("ja");
                check(L.Text("应用设置") != "应用设置" && L.Text("缺失：{0}", "AudioSwitch.exe").Contains("AudioSwitch.exe"), "Japanese messages preserve filenames");
                L.SetLanguage("missing"); check(L.Text("关闭开关") == "关闭", "unknown language retains Chinese context labels");
            }
            finally { L.SetLanguage(before); }
            string path = Path.Combine(Path.GetTempPath(), "AudioSwitch-language-" + Guid.NewGuid().ToString("N"), "settings.json");
            var current = new Preferences { Language = "ja" };
            PreferenceStore.Save(path, current);
            check(PreferenceStore.Load(path).Language == "ja", "confirmed language survives process-independent file reload");
            string backup;
            check(PreferenceStore.Import(path, "{\"DarkMode\":true}", current, out backup).Language == "ja", "importing an older backup does not repeat language onboarding");
            current.Language = L.SystemChoice;
            PreferenceStore.Save(path, current);
            check(PreferenceStore.Load(path).Language == L.SystemChoice, "saved system choice does not return to unconfirmed onboarding");
            check(PreferenceStore.Import(path, "{\"DarkMode\":true}", current, out backup).Language == L.SystemChoice, "legacy imports retain the system language choice");
        }

        // Pure UI fixtures; no backend, audio service, or application settings.
        internal static void Render(Action<bool, string> check)
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"); Directory.CreateDirectory(directory);
            string before = L.Language;
            try
            {
                foreach (string language in new[] { "zh-CN", "en", "ja" }) foreach (bool dark in new[] { true, false })
                {
                    L.SetLanguage(language);
                    var items = TrayMenuModel.Create(new AudioState(), new Preferences(), true, () => { }, () => { }, request => { }, () => { });
                    using (var popup = new TrayPopup(items, new Point(600, 600), dark, 1F, false, systemCorners: false))
                    {
                        popup.Show(); Application.DoEvents();
                        using (var bitmap = new Bitmap(popup.Width, popup.Height)) { popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, popup.Size)); bitmap.Save(Path.Combine(directory, "locale-tray-" + language + (dark ? "-dark" : "-light") + ".png")); }
                        check(popup.Width > 0 && items[0].Text == L.Text("打开主面板"), "tray locale rendered: " + language);
                        popup.Close();
                    }
                    using (var dialog = new NoticeDialog(L.Text("无法读取设备状态"), L.Text("无法连接声间后台，请确认它运行在当前 Windows 用户和会话中。"), true, false, () => dark))
                    {
                        dialog.Show(); Application.DoEvents();
                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(directory, "locale-error-" + language + (dark ? "-dark" : "-light") + ".png")); }
                        dialog.Close();
                    }
                }
            }
            finally { L.SetLanguage(before); }
        }
    }
}
