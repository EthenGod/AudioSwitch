// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    internal interface IDeviceSettingsAccess
    {
        float ReadVolume(string id);
        void SetVolume(string id, float value);
        SpatialState ReadSpatial(string id);
        void SetSpatial(string id, string format);
    }

    internal static class DeviceProfiles
    {
        internal static void Validate(DeviceProfile profile, int flow)
        {
            if (profile == null) throw new InvalidOperationException("缺少设备设置。");
            DolbyProfiles.Validate(profile.Dolby);
            if (flow != 0 && profile.Dolby != null) throw new InvalidOperationException("麦克风不适用 Dolby 播放预设。");
            if (profile.Volume.HasValue && (profile.Volume < 0 || profile.Volume > 100)) throw new InvalidOperationException("音量必须在 0–100 之间。");
            if (flow != 0 && profile.SpatialFormat != null) throw new InvalidOperationException("麦克风不适用播放空间音效。");
            Guid format;
            if (!String.IsNullOrEmpty(profile.SpatialFormat) && !Guid.TryParse(profile.SpatialFormat, out format))
                throw new InvalidOperationException("空间音效格式无效。");
        }
        private sealed class Original
        {
            internal string Id;
            internal DeviceProfile Target;
            internal float Volume;
            internal string Spatial;
            internal bool VolumeAttempted;
            internal bool SpatialAttempted;
        }
        // Configure the destination BEFORE routing sound to it, to avoid a loud first moment.
        internal static void Apply(int flow, IEnumerable<string> targets, Dictionary<string, DeviceProfile> profiles,
            IDeviceSettingsAccess access, Action selectRoles, Action<string> spatialWarning = null)
        {
            var originals = new List<Original>();
            foreach (var id in targets.Distinct())
            {
                DeviceProfile profile;
                if (profiles == null || !profiles.TryGetValue(id, out profile)) continue;
                Validate(profile, flow);
                var original = new Original { Id = id, Target = profile };
                if (profile.Volume.HasValue) original.Volume = access.ReadVolume(id);
                if (profile.SpatialFormat != null)
                {
                    var state = access.ReadSpatial(id);
                    original.Spatial = state.CurrentFormat;
                    if (!SameFormat(profile.SpatialFormat, state.CurrentFormat) && !SameFormat(profile.SpatialFormat, "")
                        && (!state.Supported || !state.Options.Any(o => SameFormat(o.Id, profile.SpatialFormat))))
                    {
                        if (spatialWarning == null) throw new InvalidOperationException("该设备目前不支持保存的空间音效，请修改设备设置。");
                        // Skip only the unavailable effect for this automatic selection; never rewrite the saved preset.
                        original.Target = new DeviceProfile { Volume = profile.Volume, SpatialFormat = null };
                        spatialWarning("暂不支持保存的空间音效，本次保留设备当前音效。可在设备设置中修改。");
                    }
                }
                originals.Add(original);
            }
            try
            {
                foreach (var original in originals)
                {
                    if (original.Target.SpatialFormat != null && !SameFormat(original.Target.SpatialFormat, original.Spatial))
                    {
                        original.SpatialAttempted = true;
                        access.SetSpatial(original.Id, original.Target.SpatialFormat);
                    }
                    if (original.Target.Volume.HasValue)
                    {
                        original.VolumeAttempted = true;
                        access.SetVolume(original.Id, original.Target.Volume.Value / 100F);
                    }
                }
                selectRoles();
            }
            catch (Exception ex)
            {
                bool incomplete = false;
                foreach (var original in originals.AsEnumerable().Reverse())
                {
                    try { if (original.SpatialAttempted) access.SetSpatial(original.Id, original.Spatial); } catch { incomplete = true; }
                    try { if (original.VolumeAttempted || (original.SpatialAttempted && original.Target.Volume.HasValue)) access.SetVolume(original.Id, original.Volume); } catch { incomplete = true; }
                }
                throw new InvalidOperationException("设备或预设应用失败：" + ex.Message + (incomplete ? " 部分音量或音效未能恢复，请检查设备。" : " 已恢复本次更改的音量和音效。"));
            }
        }
        internal static bool SameFormat(string a, string b)
        {
            Guid first, second;
            bool emptyA = String.IsNullOrEmpty(a) || (Guid.TryParse(a, out first) && first == Guid.Empty);
            bool emptyB = String.IsNullOrEmpty(b) || (Guid.TryParse(b, out second) && second == Guid.Empty);
            if (emptyA || emptyB) return emptyA == emptyB;
            return Guid.TryParse(a, out first) && Guid.TryParse(b, out second) && first == second;
        }
    }
}
