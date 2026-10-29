// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System.Drawing;
using System.Drawing.Drawing2D;
namespace AudioSwitch
{
    internal enum TrayGlyph { Panel, Browser, Output, Speaker, Monitor, Microphone, Devices, Game, Exit, Back }
    internal static class TrayArt
    {
            internal static void DrawGlyph(Graphics graphics, TrayGlyph glyph, float scale, int height, Color color)
            {
                var saved = graphics.Save();
                try {
                    // Center an 18 px optical box in the cached 42 px cell; round 1.5 px strokes.
                    graphics.TranslateTransform(12 * scale, height / 2F - 9 * scale); graphics.ScaleTransform(scale * 18 / 24, scale * 18 / 24);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var pen = new Pen(color, 2F)) {
                        pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                        switch (glyph) {
                            case TrayGlyph.Back:
                                graphics.DrawLines(pen, new[] { new PointF(12, 5), new PointF(5, 12), new PointF(12, 19) });
                                graphics.DrawLine(pen, 5, 12, 19, 12); break;
                            case TrayGlyph.Panel:
                                using (var path = Palette.Round(new RectangleF(3, 4, 18, 16), 3)) graphics.DrawPath(pen, path);
                                graphics.DrawLine(pen, 9, 4, 9, 20); break;
                            case TrayGlyph.Browser:
                                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawEllipse(pen, 8, 3, 8, 18); graphics.DrawLine(pen, 3, 12, 21, 12); break;
                            case TrayGlyph.Output:
                                graphics.DrawArc(pen, 3, 3, 18, 18, 180, 180);
                                using (var path = Palette.Round(new RectangleF(3, 12, 4, 8), 2)) graphics.DrawPath(pen, path);
                                using (var path = Palette.Round(new RectangleF(17, 12, 4, 8), 2)) graphics.DrawPath(pen, path); break;
                            case TrayGlyph.Speaker:
                                using (var path = Palette.Round(new RectangleF(5, 2, 14, 20), 2)) graphics.DrawPath(pen, path);
                                graphics.DrawEllipse(pen, 8, 10, 8, 8); graphics.DrawEllipse(pen, 11, 5, 2, 2); break;
                            case TrayGlyph.Monitor:
                                using (var path = Palette.Round(new RectangleF(2, 3, 20, 14), 2)) graphics.DrawPath(pen, path);
                                graphics.DrawLine(pen, 12, 17, 12, 21); graphics.DrawLine(pen, 8, 21, 16, 21); break;
                            case TrayGlyph.Microphone:
                                using (var path = Palette.Round(new RectangleF(9, 2, 6, 12), 3)) graphics.DrawPath(pen, path);
                                graphics.DrawArc(pen, 5, 7, 14, 12, 0, 180);
                                graphics.DrawLine(pen, 5, 10, 5, 13); graphics.DrawLine(pen, 19, 10, 19, 13);
                                graphics.DrawLine(pen, 12, 19, 12, 22); graphics.DrawLine(pen, 9, 22, 15, 22); break;
                            case TrayGlyph.Devices:
                                graphics.DrawLine(pen, 4, 7, 20, 7); graphics.DrawLines(pen, new[] { new PointF(16,3), new PointF(20,7), new PointF(16,11) });
                                graphics.DrawLine(pen, 20, 17, 4, 17); graphics.DrawLines(pen, new[] { new PointF(8,13), new PointF(4,17), new PointF(8,21) }); break;
                            case TrayGlyph.Game:
                                using (var path = new GraphicsPath()) {
                                    path.AddLine(7, 6, 17, 6);
                                    path.AddBezier(17, 6, 20, 6, 21, 9, 22, 16);
                                    path.AddBezier(22, 16, 22.5F, 19.5F, 20, 20, 18, 18);
                                    path.AddLine(18, 18, 16, 16); path.AddLine(16, 16, 8, 16); path.AddLine(8, 16, 6, 18);
                                    path.AddBezier(6, 18, 4, 20, 1.5F, 19.5F, 2, 16);
                                    path.AddBezier(2, 16, 3, 9, 4, 6, 7, 6); path.CloseFigure(); graphics.DrawPath(pen, path);
                                }
                                graphics.DrawLine(pen, 5, 11, 9, 11); graphics.DrawLine(pen, 7, 9, 7, 13);
                                using (var brush = new SolidBrush(color)) { graphics.FillEllipse(brush, 15, 9, 2, 2); graphics.FillEllipse(brush, 18, 12, 2, 2); } break;
                            case TrayGlyph.Exit:
                                graphics.DrawArc(pen, 3, 3, 18, 18, -50, 280); graphics.DrawLine(pen, 12, 2, 12, 11); break;
                        }
                    }
                } finally { graphics.Restore(saved); }
            }
    }
}
