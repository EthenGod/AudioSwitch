// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;

namespace AudioSwitch
{
    // IPC adapter only: no audio, driver, filesystem or Dolby queue access.
    internal static class PanelProfile
    {
        internal static void SaveDolby(Preferences preferences, AudioState state, Request request, Action persist)
        {
            if (request == null || String.IsNullOrEmpty(request.DeviceId) || request.Value)
                throw new InvalidOperationException("此入口仅保存 Dolby 方案，不立即应用。");
            var device = state.Devices.Concat(preferences.DeviceOrder).FirstOrDefault(d => d.Id == request.DeviceId);
            if (device == null || device.Flow != 0) throw new InvalidOperationException("请选择已记录的输出设备。");
            DeviceProfile current; preferences.DeviceProfiles.TryGetValue(request.DeviceId, out current);
            if (Wire.Encode(current == null ? null : current.Dolby) != Wire.Encode(request.ExpectedDolbyProfile))
                throw new InvalidOperationException("此设备 Dolby 方案已被其他窗口修改，请重新打开编辑器。");
            DolbyProfiles.Validate(request.DolbyProfile);
            var next = current == null ? new DeviceProfile() : Wire.Decode<DeviceProfile>(Wire.Encode(current));
            next.Dolby = request.DolbyProfile == null ? null : Wire.Decode<DolbyProfile>(Wire.Encode(request.DolbyProfile));
            try { preferences.DeviceProfiles[request.DeviceId] = next; persist(); }
            catch { if (current == null) preferences.DeviceProfiles.Remove(request.DeviceId); else preferences.DeviceProfiles[request.DeviceId] = current; throw; }
        }
        internal static void SaveBasic(Preferences preferences, AudioState state, Request request, Action persist)
        {
            if (request == null || String.IsNullOrEmpty(request.DeviceId) || request.Profile == null
                || !request.DeviceRule.HasValue || !request.ExpectedDeviceRule.HasValue || request.Value)
                throw new InvalidOperationException("基础预设请求不完整；此入口仅保存，不立即应用。");
            var device = state.Devices.Concat(preferences.DeviceOrder).FirstOrDefault(d => d.Id == request.DeviceId);
            if (device == null) throw new InvalidOperationException("设备已不在记录中，请刷新后重试。");
            if (!Enum.IsDefined(typeof(DeviceRule), request.DeviceRule.Value)) throw new InvalidOperationException("无效的白名单规则。");
            DeviceProfile current;
            preferences.DeviceProfiles.TryGetValue(request.DeviceId, out current);
            if ((current == null) != (request.ExpectedProfile == null)
                || (current != null && (current.Volume != request.ExpectedProfile.Volume || current.SpatialFormat != request.ExpectedProfile.SpatialFormat))
                || DeviceAutomation.Rule(preferences, request.DeviceId) != request.ExpectedDeviceRule.Value)
                throw new InvalidOperationException("此设备预设已被其他窗口修改，请关闭设置并重新打开后再保存。");
            // Never accept a Dolby payload from this editor. Preserve the latest backend copy.
            if (request.Profile.Dolby != null) throw new InvalidOperationException("基础预设不能修改 Dolby 参数。");
            var next = current == null ? new DeviceProfile() : Wire.Decode<DeviceProfile>(Wire.Encode(current));
            next.Volume = request.Profile.Volume;
            next.SpatialFormat = device.Flow == 1 && current != null ? current.SpatialFormat : request.Profile.SpatialFormat;
            DeviceProfiles.Validate(next, device.Flow);
            var originalRule = DeviceAutomation.Rule(preferences, request.DeviceId);
            try
            {
                preferences.DeviceProfiles[request.DeviceId] = next;
                if (request.DeviceRule.Value == DeviceRule.Normal) preferences.DeviceRules.Remove(request.DeviceId);
                else preferences.DeviceRules[request.DeviceId] = request.DeviceRule.Value;
                persist();
            }
            catch
            {
                if (current == null) preferences.DeviceProfiles.Remove(request.DeviceId);
                else preferences.DeviceProfiles[request.DeviceId] = current;
                if (originalRule == DeviceRule.Normal) preferences.DeviceRules.Remove(request.DeviceId);
                else preferences.DeviceRules[request.DeviceId] = originalRule;
                throw;
            }
        }
    }
}
