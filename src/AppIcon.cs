// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AudioSwitch
{
    internal static class AppIcon
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        internal static Icon Create(int size = 32)
        {
            size = Math.Max(16, Math.Min(64, size));
            using (var bitmap = new Bitmap(size, size))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                DrawMark(graphics, size, Color.FromArgb(0, 102, 220));
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        internal static void DrawMark(Graphics graphics, float size, Color background)
        {
            var state = graphics.Save();
            try {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(size / 48F, size / 48F);
                using (var path = Palette.Round(new RectangleF(0, 0, 47, 47), 13))
                using (var brush = new SolidBrush(background)) graphics.FillPath(brush, path);
                using (var pen = new Pen(Color.White, 3)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    int[] heights = { 9, 18, 27, 14, 21 };
                    for (int i = 0; i < heights.Length; i++) graphics.DrawLine(pen, 10 + i * 7, (48 - heights[i]) / 2F, 10 + i * 7, (48 + heights[i]) / 2F);
                }
            } finally { graphics.Restore(state); }
        }
    }
}
