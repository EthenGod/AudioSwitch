// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AudioSwitch
{
    internal sealed class LanguageInfo
    {
        public string id { get; set; }
        public string name { get; set; }
        public string[] match { get; set; }
    }
    internal static class L
    {
        internal const string DefaultLanguage = "zh-CN";
        internal const string SystemChoice = "system";
        private sealed class Selection
        {
            internal string Id;
            internal Dictionary<string, string> Messages;
        }
        internal static readonly LanguageInfo[] Languages = Read<LanguageInfo[]>("languages") ?? new[] {
            new LanguageInfo { id = DefaultLanguage, name = "简体中文", match = new[] { "zh" } }
        };
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> chinese = Read<Dictionary<string, string>>(DefaultLanguage) ?? new Dictionary<string, string>();
        private static volatile Selection current = new Selection { Id = DefaultLanguage, Messages = chinese };
        private static readonly Regex parameter = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);
        internal static string Language { get { return current.Id; } }
        internal static string Match(IEnumerable<string> preferred)
        {
            foreach (var value in preferred)
            {
                if (String.IsNullOrEmpty(value)) continue;
                string tag = value.Replace('_', '-');
                var exact = Languages.FirstOrDefault(item => String.Equals(item.id, tag, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact.id;
                var match = Languages.FirstOrDefault(item => item.match.Any(prefix => String.Equals(tag, prefix, StringComparison.OrdinalIgnoreCase) || tag.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)));
                if (match != null) return match.id;
            }
            return DefaultLanguage;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[] buffer, ref uint length);
        private static volatile string systemLanguage = DetectSystemLanguage();
        internal static string SystemLanguage { get { return systemLanguage; } }
        // Read Windows again only on a preference notification or an explicit system selection.
        internal static void RefreshSystemLanguage() { systemLanguage = DetectSystemLanguage(); }
        private static string DetectSystemLanguage()
        {
            try
            {
                uint count, length = 0;
                if (GetUserPreferredUILanguages(8, out count, null, ref length) && length > 0 && length <= 32768)
                {
                    var buffer = new char[length];
                    if (GetUserPreferredUILanguages(8, out count, buffer, ref length))
                        return Match(new String(buffer).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            catch (EntryPointNotFoundException) { }
            return Match(new[] { CultureInfo.CurrentUICulture.Name, CultureInfo.InstalledUICulture.Name });
        }
        internal static bool ValidId(string id) { return id != null && id.Length <= 35 && Regex.IsMatch(id, @"\A[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\z", RegexOptions.CultureInvariant); }
        internal static bool ValidChoice(string value) { return value == SystemChoice || ValidId(value); }
        internal static bool SupportedChoice(string value) { return value == SystemChoice || Languages.Any(item => item.id == value); }
        internal static string Resolve(string saved) { return Resolve(saved, SystemLanguage); }
        internal static string Resolve(string saved, string system)
        {
            string id = saved == null || saved == SystemChoice ? system : saved;
            return Languages.Any(item => item.id == id) ? id : DefaultLanguage;
        }
        internal static void SetLanguage(string saved)
        {
            string id = Resolve(saved);
            if (current.Id == id) return;
            lock (gate)
            {
                Dictionary<string, string> messages = chinese;
                if (id != DefaultLanguage && !catalogs.TryGetValue(id, out messages))
                {
                    messages = Read<Dictionary<string, string>>(id) ?? new Dictionary<string, string>();
                    catalogs[id] = messages;
                }
                current = new Selection { Id = id, Messages = messages };
            }
        }
        // Runs once per process, including short-lived helpers. Never migrates or writes settings.
        internal static void Initialize()
        {
            string saved = null;
            try
            {
                string path = PreferenceStore.SettingsPath;
                if (File.Exists(path) && new FileInfo(path).Length <= PreferenceStore.MaxBytes)
                {
                    var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                    object settings, value;
                    if (root != null && root.TryGetValue("Settings", out settings)) root = settings as Dictionary<string, object>;
                    if (root != null && root.TryGetValue("Language", out value)) saved = value as string;
                }
            }
            catch { } // A corrupt configuration must not suppress its error window.
            SetLanguage(saved);
        }
        internal static string Text(string source, params object[] args)
        {
            var selection = current;
            string value;
            if (selection.Messages == null || !selection.Messages.TryGetValue(source, out value) || String.IsNullOrEmpty(value))
                if (!chinese.TryGetValue(source, out value)) value = source;
            return args.Length == 0 ? value : parameter.Replace(value, m => {
                int index;
                return Int32.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < args.Length ? Convert.ToString(args[index], CultureInfo.CurrentCulture) : m.Value;
            });
        }
        private static T Read<T>(string name) where T : class
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AudioSwitch.Locales." + name))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<T>(reader.ReadToEnd());
            }
        }
    }
}
