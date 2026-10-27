// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace AudioSwitch
{
    internal static class GameModeMenu
    {
        internal static TrayCommand Create(bool enabled, Action<bool> change)
        {
            return new TrayCommand("游戏模式", TrayGlyph.Game, () => change(!enabled)) { Toggle = true, Checked = enabled };
        }
    }
}
