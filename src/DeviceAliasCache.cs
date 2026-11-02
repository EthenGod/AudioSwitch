// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AudioSwitch
{
    internal static class DeviceAliasCache
    {
        private const string FileName = "device-name-cache.json";

        internal static List<string> Load(string directory, List<string> previous)
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                // Upgrade only from the local active settings, never from an imported backup.
                if (previous.Count > 0) Save(directory, previous);
                return previous;
            }
            try
            {
                if (new FileInfo(path).Length > 16384) throw new InvalidDataException();
                var rows = Wire.Decode<object[]>(File.ReadAllText(path));
                if (rows == null || rows.Length > Preferences.AliasHistoryLimit ||
                    rows.Any(row => !PreferenceStore.ValidAlias(row as string))) throw new InvalidDataException();
                return rows.Cast<string>().Distinct(StringComparer.Ordinal).ToList();
            }
            catch (Exception) { return new List<string>(); } // An unreadable suggestion cache must not block startup.
        }

        internal static void Save(string directory, List<string> names)
        {
            try { PreferenceStore.WriteAtomic(Path.Combine(directory, FileName), Wire.Encode(names)); }
            catch (Exception ex) { Program.Log(ex); } // The device rename has already been committed.
        }
    }
}
