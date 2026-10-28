// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    internal static class TrayMenuModel
    {
        internal static IList<TrayCommand> Create(AudioState state, Preferences preferences, bool browser, int pending,
            Action<bool> open, Action openBrowser, Action<Request> request, Action exit)
        {
            var items = new List<TrayCommand> {
                new TrayCommand("打开主面板", TrayGlyph.Panel, () => open(false)) { Key = "panel", AnimateClose = true },
                new TrayCommand("在浏览器打开", TrayGlyph.Browser, openBrowser) { Key = "browser", Enabled = browser, AnimateClose = true }
            };
            if (pending > 0) items.Add(new TrayCommand("处理设备变化", TrayGlyph.Devices, () => open(true)) { Key = "pending", BreakBefore = true, AnimateClose = true });
            foreach (int flow in new[] { 0, 1 }) {
                string current = state.Default(flow, 1);
                var children = new List<TrayCommand>();
                foreach (var device in DevicePriority.Ordered(state.Devices, preferences, state, flow)) {
                    string id = device.Id;
                    children.Add(new TrayCommand(device.Name, flow == 0 ? TrayGlyph.Output : TrayGlyph.Microphone, () => request(new Request { Action = "switch", DeviceId = id })) { Key = "device:" + id, KeepOpen = true, Checked = current == id });
                }
                if (children.Count == 0) children.Add(new TrayCommand("暂无可用设备", flow == 0 ? TrayGlyph.Output : TrayGlyph.Microphone) { Key = "empty:" + flow, Enabled = false });
                var selected = state.Devices.FirstOrDefault(d => d.Id == current);
                items.Add(new TrayCommand(flow == 0 ? "声音输出" : "麦克风输入", flow == 0 ? TrayGlyph.Output : TrayGlyph.Microphone) {
                    Key = "flow:" + flow, Detail = selected == null ? "暂无默认设备" : selected.Name, Children = children, BreakBefore = flow == 0
                });
            }
            bool priority = preferences.UseDevicePriority;
            items.Add(new TrayCommand("自动切换", TrayGlyph.Devices, () => request(new Request { Action = "priority", Value = !priority })) { Key = "priority", KeepOpen = true, Toggle = true, Checked = priority, BreakBefore = true });
            items.Add(GameModeMenu.Create(preferences.GameMode, value => request(new Request { Action = "gameMode", Value = value })));
            items.Add(new TrayCommand("退出声间", TrayGlyph.Exit, exit) { Key = "exit", BreakBefore = true });
            return items;
        }
    }
}
