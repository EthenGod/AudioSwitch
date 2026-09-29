// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class GameModeMenu
    {
        internal static ToolStripMenuItem Create(bool enabled, Action<bool> change)
        {
            var item = new ToolStripMenuItem("游戏模式") { Name = "gameMode", Checked = enabled,
                ToolTipText = "暂停自动更新、后台下载和负载检测；设备切换与音效跟随照常工作" };
            item.Click += delegate { change(!enabled); };
            return item;
        }
    }
}
