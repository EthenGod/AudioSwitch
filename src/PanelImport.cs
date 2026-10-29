// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AudioSwitch
{
    internal static class PanelImport
    {
        internal static string Revision(Preferences current)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PreferenceStore.Export(current)))).Replace("-", "");
        }
        internal static Reply Preview(string json, Preferences current, AudioState state)
        {
            Preferences incoming;
            try { incoming = PreferenceStore.Parse(json); }
            catch (InvalidOperationException)
            {
                // Parser exceptions can include backup contents. Keep the UI error
                // actionable and do not forward that payload through an inner exception.
                throw new InvalidOperationException("备份内容损坏或格式不受支持，请重新选择声间导出的备份。当前配置未改变。");
            }
            return new Reply { PanelApiVersion = 2, ConfigurationJson = PreferenceStore.Export(incoming), ConfigurationRevision = Revision(current),
                ImportPreview = new ImportPreviewInfo {
                    Devices = incoming.DeviceOrder.Count, Profiles = incoming.DeviceProfiles.Count, Rules = incoming.DeviceRules.Count,
                    DolbyProfiles = incoming.DeviceProfiles.Values.Count(p => p.Dolby != null),
                    OfflineDevices = incoming.DeviceOrder.Count(d => !state.Devices.Any(active => active.Id == d.Id))
                } };
        }
        internal static void RequireUnchanged(Preferences current, string revision)
        {
            if (String.IsNullOrEmpty(revision) || Revision(current) != revision)
                throw new InvalidOperationException("预览后配置已变化，请重新选择备份并确认，避免覆盖刚才的修改。");
        }
    }
}
