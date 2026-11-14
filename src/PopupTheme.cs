// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;

namespace AudioSwitch
{
    // Native companion to frontend/src/appearance/themes.css. No WebView or CSS interpreter.
    internal sealed class PopupTheme
    {
        internal readonly bool Dark;
        internal readonly string StyleId;
        internal readonly Color Background, Card, Text, Muted, Border, Hover, Pressed, Accent, Selected, AccentText, Danger;
        // Add a native palette here when a web style should also change the tray.
        // An unavailable palette falls back without overwriting the saved preference.
        internal static string ResolveStyle(string style) { return "classic"; }
        internal PopupTheme(bool dark, string style = "classic") {
            Dark = dark; StyleId = ResolveStyle(style);
            Background = Hex(dark ? "#111113" : "#fcfcfd"); Danger = Hex(dark ? "#ff9592" : "#ce2c31");
            Card = Hex(dark ? "#18191b" : "#f9f9fb"); Text = Hex(dark ? "#edeef0" : "#1c2024"); Muted = Hex(dark ? "#b0b4ba" : "#60646c");
            Border = Hex(dark ? "#363a3f" : "#d9d9e0"); Hover = Hex(dark ? "#272a2d" : "#e8e8ec"); Pressed = Hex(dark ? "#2e3135" : "#e0e1e6");
            Accent = Hex("#3e63dd"); Selected = Hex(dark ? "#182449" : "#edf2fe"); AccentText = Hex(dark ? "#9eb1ff" : "#3a5bc7");
        }
        private static Color Hex(string text) { return ColorTranslator.FromHtml(text); }
        internal static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
    }
}
