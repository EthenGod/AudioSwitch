// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    // Saved IDs retain their positions while offline. New endpoints go to the end.
    internal static class DevicePriority
    {
        internal static bool Remember(Preferences preferences, AudioState state)
        {
            string before = Wire.Encode(preferences.DeviceOrder);
            var known = (preferences.DeviceOrder ?? new List<Endpoint>())
                .Where(d => d != null && !String.IsNullOrEmpty(d.Id) && (d.Flow == 0 || d.Flow == 1))
                .GroupBy(d => d.Id).Select(g => g.First()).ToList();
            foreach (int flow in new[] { 0, 1 })
            {
                bool first = !known.Any(d => d.Flow == flow);
                var online = state.Devices.Where(d => d.Flow == flow)
                    .OrderByDescending(d => first && d.Id == state.Default(flow, 1)).ThenBy(d => d.Name).ThenBy(d => d.Id);
                foreach (var device in online)
                {
                    var saved = known.FirstOrDefault(d => d.Id == device.Id);
                    if (saved == null) known.Add(new Endpoint { Id = device.Id, Name = device.Name, Flow = flow });
                    else { saved.Name = device.Name; saved.Flow = flow; }
                }
            }
            preferences.DeviceOrder = known;
            return before != Wire.Encode(known);
        }

        internal static List<Endpoint> Ordered(IEnumerable<Endpoint> devices, Preferences preferences, AudioState state, int flow)
        {
            var order = (preferences.DeviceOrder ?? new List<Endpoint>()).Where(d => d.Flow == flow).Select(d => d.Id).ToList();
            return devices.Where(d => d.Flow == flow).OrderBy(d => {
                int index = order.IndexOf(d.Id);
                return index < 0 ? Int32.MaxValue : index;
            }).ThenByDescending(d => order.Count == 0 && d.Id == state.Default(flow, 1)).ThenBy(d => d.Name).ThenBy(d => d.Id).ToList();
        }

        internal static void Reorder(Preferences preferences, int flow, List<string> ids)
        {
            if (flow != 0 && flow != 1) throw new InvalidOperationException("无效的设备类型。");
            var known = preferences.DeviceOrder.Where(d => d.Flow == flow).ToList();
            if (ids == null || ids.Count != ids.Distinct().Count() || ids.Any(id => !known.Any(d => d.Id == id)))
                throw new InvalidOperationException("设备列表已变化，请重新打开优先级设置。");
            // Preserve endpoints discovered while the editor was open, after the submitted order.
            var arranged = ids.Select(id => known.Single(d => d.Id == id)).Concat(known.Where(d => !ids.Contains(d.Id))).ToList();
            preferences.DeviceOrder = preferences.DeviceOrder.Where(d => d.Flow != flow).Concat(arranged).ToList();
        }

        internal static List<Endpoint> Targets(AudioState previous, AudioState next, Preferences preferences, int? forceFlow = null, bool startup = false)
        {
            var targets = new List<Endpoint>();
            if (!preferences.UseDevicePriority) return targets;
            foreach (int flow in new[] { 0, 1 })
            {
                var oldIds = new HashSet<string>(previous.Devices.Where(d => d.Flow == flow).Select(d => d.Id));
                bool changed = !oldIds.SetEquals(next.Devices.Where(d => d.Flow == flow).Select(d => d.Id));
                bool force = forceFlow == flow || forceFlow == -1;
                if (!changed && !force && !startup) continue;
                var top = Ordered(next.Devices, preferences, next, flow).FirstOrDefault();
                if (top == null) continue;
                var roles = preferences.IncludeCommunications ? new[] { 0, 1, 2 } : new[] { 0, 1 };
                // Even if Windows already picked the winner during hot-plug, apply its saved profile.
                if (changed || force || roles.Any(role => next.Default(flow, role) != top.Id)) targets.Add(top);
            }
            return targets;
        }
    }
}
