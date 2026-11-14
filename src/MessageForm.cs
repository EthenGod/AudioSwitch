// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioSwitch
{
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
