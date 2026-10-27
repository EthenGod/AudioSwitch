// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System.Drawing;
using System.Drawing.Drawing2D;
namespace AudioSwitch
{
    internal enum TrayGlyph { Panel, Browser, Output, Microphone, Devices, Game, Exit }
    internal static class TrayArt
    {
            internal static void DrawGlyph(Graphics graphics, TrayGlyph glyph, float scale, int height, Color color)
            {
                var saved = graphics.Save();
                try {
                    // One 24-unit grid, 20 px optical box and a shared 1.5 px stroke.
                    graphics.TranslateTransform(11 * scale, height / 2F - 10 * scale); graphics.ScaleTransform(scale * 20 / 24, scale * 20 / 24);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var pen = new Pen(color, 1.8F)) {
                        pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                        switch (glyph) {
                            case TrayGlyph.Panel:
                                using (var path = Palette.Round(new RectangleF(3, 4, 18, 16), 3)) graphics.DrawPath(pen, path);
                                graphics.DrawLine(pen, 9, 4, 9, 20); break;
                            case TrayGlyph.Browser:
                                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawEllipse(pen, 8, 3, 8, 18); graphics.DrawLine(pen, 3, 12, 21, 12); break;
                            case TrayGlyph.Output:
                                graphics.DrawArc(pen, 3, 3, 18, 18, 180, 180);
                                using (var path = Palette.Round(new RectangleF(3, 12, 4, 8), 2)) graphics.DrawPath(pen, path);
                                using (var path = Palette.Round(new RectangleF(17, 12, 4, 8), 2)) graphics.DrawPath(pen, path); break;
                            case TrayGlyph.Microphone:
                                using (var path = Palette.Round(new RectangleF(9, 2, 6, 13), 3)) graphics.DrawPath(pen, path);
                                graphics.DrawArc(pen, 5, 5, 14, 14, 0, 180); graphics.DrawLine(pen, 12, 19, 12, 22); graphics.DrawLine(pen, 9, 22, 15, 22); break;
                            case TrayGlyph.Devices:
                                graphics.DrawLine(pen, 4, 7, 20, 7); graphics.DrawLines(pen, new[] { new PointF(16,3), new PointF(20,7), new PointF(16,11) });
                                graphics.DrawLine(pen, 20, 17, 4, 17); graphics.DrawLines(pen, new[] { new PointF(8,13), new PointF(4,17), new PointF(8,21) }); break;
                            case TrayGlyph.Game:
                                using (var path = new GraphicsPath()) {
                                    path.AddBezier(7, 6, 3, 6, 3, 9, 2, 16); path.AddBezier(2, 16, 1, 22, 6, 20, 9, 16);
                                    path.AddLine(9, 16, 15, 16); path.AddBezier(15, 16, 18, 20, 23, 22, 22, 16);
                                    path.AddBezier(22, 16, 21, 9, 21, 6, 17, 6); path.CloseFigure(); graphics.DrawPath(pen, path);
                                }
                                graphics.DrawLine(pen, 5, 11, 9, 11); graphics.DrawLine(pen, 7, 9, 7, 13);
                                graphics.DrawLine(pen, 16, 10, 16.1F, 10); graphics.DrawLine(pen, 19, 13, 19.1F, 13); break;
                            case TrayGlyph.Exit:
                                graphics.DrawArc(pen, 3, 3, 18, 18, -50, 280); graphics.DrawLine(pen, 12, 2, 12, 11); break;
                        }
                    }
                } finally { graphics.Restore(saved); }
            }
    }
}
