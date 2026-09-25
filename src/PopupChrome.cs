// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AudioSwitch
{
    internal static class PopupChrome
    {
        [ThreadStatic] private static bool rounding;
        [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        internal static void RemoveNativeFrame(IntPtr window)
        {
            int style = GetWindowLong(window, -16), extended = GetWindowLong(window, -20);
            int plainStyle = style & ~0x00C00000, plainExtended = extended & ~0x00020300;
            if (plainStyle == style && plainExtended == extended) return;
            SetWindowLong(window, -16, plainStyle); SetWindowLong(window, -20, plainExtended);
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        }
        internal static void Round(IntPtr window)
        {
            if (rounding) return;
            Rect rect; if (!GetWindowRect(window, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
            rounding = true;
            try {
            using (var graphics = Graphics.FromHwnd(window))
            {
                int diameter = (int)(16 * graphics.DpiX / 96F);
                IntPtr region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
                if (region != IntPtr.Zero && SetWindowRgn(window, region, false) == 0) DeleteObject(region);
                // Windows owns the region after successful SetWindowRgn.
            }
            } finally { rounding = false; }
        }
        internal static void Border(IntPtr window, IntPtr destination = default(IntPtr))
        {
            Rect rect; if (!GetWindowRect(window, out rect) || rect.Right - rect.Left < 2 || rect.Bottom - rect.Top < 2) return;
            IntPtr dc = destination == IntPtr.Zero ? GetWindowDC(window) : destination; if (dc == IntPtr.Zero) return;
            try {
                using (var graphics = Graphics.FromHdc(dc)) DrawBorder(graphics, new Size(rect.Right - rect.Left, rect.Bottom - rect.Top));
            } finally { if (destination == IntPtr.Zero) ReleaseDC(window, dc); }
        }
        internal static void DrawBorder(Graphics graphics, Size size)
        {
            if (size.Width < 10 || size.Height < 10) return;
            var state = graphics.Save();
            try {
                float radius = 7 * graphics.DpiX / 96F;
                using (var inner = Palette.Round(new RectangleF(4, 4, size.Width - 8, size.Height - 8), Math.Max(1, radius - 2.5F))) {
                    // Clear the whole rim before antialiasing. Repeated paints must not
                    // accumulate dark pixels at the corners or retain a previous highlight.
                    var rimState = graphics.Save();
                    using (var region = new Region(inner)) graphics.ExcludeClip(region);
                    using (var brush = new SolidBrush(Palette.Card)) graphics.FillRectangle(brush, 0, 0, size.Width, size.Height);
                    graphics.Restore(rimState);
                }
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var outline = Palette.Round(new RectangleF(1.5F, 1.5F, size.Width - 3, size.Height - 3), radius))
                using (var pen = new Pen(Palette.Border)) graphics.DrawPath(pen, outline);
            } finally { graphics.Restore(state); }
        }
        internal static Size WindowSize(IntPtr window)
        {
            Rect rect;
            return GetWindowRect(window, out rect) ? new Size(rect.Right - rect.Left, rect.Bottom - rect.Top) : Size.Empty;
        }
    }
}
