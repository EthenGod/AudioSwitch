// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AudioSwitch
{
    public sealed class ConfigurationFile
    {
        public string Format { get; set; }
        public int Version { get; set; }
        public Preferences Settings { get; set; }
    }
    internal static class PreferenceStore
    {
        internal const int MaxBytes = 1024 * 1024;
        internal static string SettingsPath { get { return Path.Combine(Program.DataDirectory, "settings.json"); } }
        internal static Preferences Load(string path)
        {
            if (!File.Exists(path)) return new Preferences();
            string text = ReadFile(path); bool legacy;
            var result = Parse(text, out legacy);
            if (legacy) { Backup(path, result, "before-migration"); Save(path, result); }
            return result;
        }
        internal static string ReadFile(string path)
        {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidOperationException("配置文件最大支持 1 MiB。");
            return File.ReadAllText(path, new UTF8Encoding(false, true));
        }
        internal static Preferences Parse(string text) { bool legacy; return Parse(text, out legacy); }
        private static Preferences Parse(string text, out bool legacy)
        {
            legacy = false;
            try
            {
                if (String.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidOperationException("配置为空或超过 1 MiB。");
                var root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
                if (root == null) throw new InvalidOperationException("配置必须是 JSON 对象。");
                Dictionary<string, object> settings;
                if (root.ContainsKey("Format") || root.ContainsKey("Version") || root.ContainsKey("Settings"))
                {
                    Keys(root, "Format", "Version", "Settings");
                    if (!root.ContainsKey("Format") || !Object.Equals(root["Format"], "AudioSwitch.Settings")) throw new InvalidOperationException("这不是声间的配置文件。");
                    if (!root.ContainsKey("Version") || !(root["Version"] is int) || (int)root["Version"] != 1) throw new InvalidOperationException("不支持此配置版本。");
                    if (!root.ContainsKey("Settings") || (settings = root["Settings"] as Dictionary<string, object>) == null) throw new InvalidOperationException("缺少 Settings 内容。");
                }
                else { legacy = true; settings = root; }
                Shape(settings);
                var result = Wire.Decode<Preferences>(Wire.Encode(settings));
                Validate(result);
                return result;
            }
            catch (Exception ex) { throw new InvalidOperationException("无法读取配置：" + ex.Message, ex); }
        }
        private static void Keys(Dictionary<string, object> map, params string[] allowed)
        {
            if (map.Keys.Any(k => !allowed.Contains(k))) throw new InvalidOperationException("存在不支持的配置字段。");
        }
        private static void Shape(Dictionary<string, object> settings)
        {
            if (settings.Count == 0) throw new InvalidOperationException("缺少设置内容。");
            Keys(settings, "AskOnConnect", "IncludeCommunications", "UseDevicePriority", "DarkMode", "DeviceProfiles", "DeviceOrder", "DeviceRules");
            foreach (string key in new[] { "AskOnConnect", "IncludeCommunications", "UseDevicePriority", "DarkMode" })
                if (settings.ContainsKey(key) && !(settings[key] is bool)) throw new InvalidOperationException(key + " 必须为 true 或 false。");
            foreach (string key in new[] { "DeviceProfiles", "DeviceRules" })
            {
                if (!settings.ContainsKey(key) || settings[key] == null) continue;
                var map = settings[key] as Dictionary<string, object>;
                if (map == null) throw new InvalidOperationException(key + " 必须是对象。");
                foreach (var pair in map)
                {
                    if (key == "DeviceRules")
                    {
                        if (!(pair.Value is int) || !Enum.IsDefined(typeof(DeviceRule), pair.Value)) throw new InvalidOperationException("白名单规则无效。");
                    }
                    else
                    {
                        var profile = pair.Value as Dictionary<string, object>;
                        if (profile == null) throw new InvalidOperationException("设备预设无效。");
                        Keys(profile, "Volume", "SpatialFormat", "Dolby");
                        if (profile.ContainsKey("Dolby") && profile["Dolby"] != null) DolbyProfiles.ValidateShape(profile["Dolby"]);
                        if (profile.ContainsKey("Volume") && profile["Volume"] != null && !(profile["Volume"] is int)) throw new InvalidOperationException("音量必须为整数。");
                        if (profile.ContainsKey("SpatialFormat") && profile["SpatialFormat"] != null && !(profile["SpatialFormat"] is string)) throw new InvalidOperationException("空间音效格式必须为字符串。");
                    }
                }
            }
            if (settings.ContainsKey("DeviceOrder") && settings["DeviceOrder"] != null)
            {
                var order = settings["DeviceOrder"] as object[];
                if (order == null) throw new InvalidOperationException("设备排序必须是数组。");
                foreach (var item in order)
                {
                    var device = item as Dictionary<string, object>;
                    if (device == null) throw new InvalidOperationException("设备排序项无效。");
                    Keys(device, "Id", "Name", "Flow");
                    if (!device.ContainsKey("Id") || !(device["Id"] is string) || !device.ContainsKey("Name") || !(device["Name"] is string)
                        || !device.ContainsKey("Flow") || !(device["Flow"] is int)) throw new InvalidOperationException("设备需要 Id、Name 和整数 Flow。");
                }
            }
        }
        private static void Validate(Preferences preferences)
        {
            if (preferences == null) throw new InvalidOperationException("缺少配置。");
            if (preferences.DeviceProfiles == null) preferences.DeviceProfiles = new Dictionary<string, DeviceProfile>();
            if (preferences.DeviceRules == null) preferences.DeviceRules = new Dictionary<string, DeviceRule>();
            if (preferences.DeviceOrder == null) preferences.DeviceOrder = new List<Endpoint>();
            var ids = new HashSet<string>();
            foreach (var device in preferences.DeviceOrder)
                if (device == null || String.IsNullOrWhiteSpace(device.Id) || String.IsNullOrWhiteSpace(device.Name) || (device.Flow != 0 && device.Flow != 1) || !ids.Add(device.Id))
                    throw new InvalidOperationException("设备排序包含无效或重复设备。");
            foreach (var pair in preferences.DeviceProfiles)
            {
                if (String.IsNullOrWhiteSpace(pair.Key)) throw new InvalidOperationException("预设缺少设备 ID。");
                var device = preferences.DeviceOrder.FirstOrDefault(d => d.Id == pair.Key);
                DeviceProfiles.Validate(pair.Value, device != null ? device.Flow : pair.Key.StartsWith("{0.0.1.") ? 1 : 0);
            }
            if (preferences.DeviceRules.Any(p => String.IsNullOrWhiteSpace(p.Key) || !Enum.IsDefined(typeof(DeviceRule), p.Value))) throw new InvalidOperationException("白名单规则无效。");
        }
        internal static string Export(Preferences preferences)
        {
            var copy = Wire.Decode<Preferences>(Wire.Encode(preferences)); Validate(copy);
            string json = Pretty(Wire.Encode(new ConfigurationFile { Format = "AudioSwitch.Settings", Version = 1, Settings = copy }));
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidOperationException("配置超过 1 MiB，无法保存。");
            return json;
        }
        internal static void Save(string path, Preferences preferences) { WriteAtomic(path, Export(preferences)); }
        internal static void WriteAtomic(string path, string json)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        private static string Backup(string path, Preferences current, string reason)
        {
            string backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "backups", reason + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            Save(backup, current); return backup;
        }
        internal static Preferences Import(string path, string json, Preferences current, out string backupPath)
        {
            var imported = Parse(json); string prepared = Export(imported);
            backupPath = Backup(path, current, "before-import");
            WriteAtomic(path, prepared);
            return imported;
        }
        private static string Pretty(string json)
        {
            var output = new StringBuilder(); int depth = 0; bool quoted = false, escaped = false;
            foreach (char c in json)
            {
                if (quoted) { output.Append(c); if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                if (c == '"') { quoted = true; output.Append(c); }
                else if (c == '{' || c == '[') { output.Append(c).AppendLine(); depth++; output.Append(' ', depth * 2); }
                else if (c == '}' || c == ']') { depth--; output.AppendLine().Append(' ', depth * 2).Append(c); }
                else if (c == ',') output.Append(c).AppendLine().Append(' ', depth * 2);
                else if (c == ':') output.Append(": ");
                else if (!Char.IsWhiteSpace(c)) output.Append(c);
            }
            return output.AppendLine().ToString();
        }
    }
}
