// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioSwitch
{
    // Same surface, border and accent steps as the panel and tray.
    internal sealed class PopupTheme
    {
        internal readonly bool Dark;
        internal readonly Color Background, Card, Text, Muted, Border, Hover, Pressed, Accent, Selected, AccentText, Danger;
        internal PopupTheme(bool dark) {
            Dark = dark; Background = Hex(dark ? "#111113" : "#fcfcfd"); Danger = Hex(dark ? "#ff9592" : "#ce2c31");
            Card = Hex(dark ? "#18191b" : "#f9f9fb"); Text = Hex(dark ? "#edeef0" : "#1c2024"); Muted = Hex(dark ? "#b0b4ba" : "#60646c");
            Border = Hex(dark ? "#363a3f" : "#d9d9e0"); Hover = Hex(dark ? "#272a2d" : "#e8e8ec"); Pressed = Hex(dark ? "#2e3135" : "#e0e1e6");
            Accent = Hex("#3e63dd"); Selected = Hex(dark ? "#182449" : "#edf2fe"); AccentText = Hex(dark ? "#9eb1ff" : "#3a5bc7");
        }
        private static Color Hex(string text) { return ColorTranslator.FromHtml(text); }
        internal static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
    }

    internal class MessageForm : Form
    {
        private readonly Func<bool> readDark;
        internal PopupTheme Theme { get; private set; }
        protected MessageForm(bool followSystem, Func<bool> systemDark = null)
        {
            readDark = followSystem ? systemDark ?? ReadSystemDark : (Func<bool>)(() => Palette.Dark);
            RefreshTheme();
        }
        internal static bool SystemDark(object value) { return !Object.Equals(value, 1); }
        private static bool ReadSystemDark()
        {
            try {
                return SystemDark(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null));
            } catch (System.Security.SecurityException) { return true; }
              catch (UnauthorizedAccessException) { return true; }
              catch (System.IO.IOException) { return true; }
        }
        internal static PopupTheme ColorsOf(Control control)
        {
            var form = control.FindForm() as MessageForm;
            return form == null ? null : form.Theme;
        }
        internal void RefreshTheme()
        {
            bool dark = readDark();
            if (Theme != null && Theme.Dark == dark) return;
            Theme = new PopupTheme(dark);
            BackColor = Theme.Background; ForeColor = Theme.Text;
            StyleChildren(this);
            Palette.WindowChrome(this); Invalidate(true);
        }
        private void StyleChildren(Control parent)
        {
            foreach (Control child in parent.Controls) {
                var card = child as Surface;
                if (card != null) { card.Fill = Theme.Card; card.Stroke = Theme.Border; }
                if (child is ScrollCanvas || child is TextBox) child.BackColor = Theme.Card;
                if (child is Label || child is TextBox) child.ForeColor = Theme.Text;
                if (child is TextBox) Palette.NativeChrome(child);
                StyleChildren(child);
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            StyleChildren(this); Palette.WindowChrome(this);
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x001a && readDark != null) RefreshTheme(); // WM_SETTINGCHANGE
        }
    }
}
