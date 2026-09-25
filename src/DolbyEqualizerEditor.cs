// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class DolbyEqualizerEditor : UserControl
    {
        private readonly NumberBox[] raw = new NumberBox[20];
        private readonly NumberBox[] db = new NumberBox[10];
        private readonly LevelSlider[] sliders = new LevelSlider[10];
        private bool loading;
        internal DolbyEqualizerEditor()
        {
            SuspendLayout();
            Size = new Size(500, 340); BackColor = Palette.Card;
            var mode = new ChoiceBox { Name = "dolbyEqView", AccessibleName = "均衡器编辑方式" };
            mode.Items.AddRange(new[] { "10 点 · dB（Dolby Access 方式）", "20 段 · 原始值（高级）" }); mode.SetBounds(0, 0, 340, 30); Controls.Add(mode);
            var ten = new Panel(); ten.SetBounds(0, 40, 500, 234); Controls.Add(ten);
            var twenty = new Panel(); twenty.SetBounds(0, 40, 500, 244); Controls.Add(twenty);
            ten.SuspendLayout(); twenty.SuspendLayout();
            for (int i = 0; i < 10; i++)
            {
                int slot = i, x = i * 50;
                LabelAt(ten, DolbyEqualizer.Labels[i], x, 0, 48, true);
                sliders[i] = new LevelSlider { Name = "dolbySlider" + i, AccessibleName = DolbyEqualizer.Labels[i] + " 均衡器，dB",
                    Vertical = true, Minimum = -1200, Maximum = 1200, SmallChange = 10, DisplayDivisor = 100, BackColor = Palette.Card };
                sliders[i].SetBounds(x + 5, 27, 38, 152); ten.Controls.Add(sliders[i]);
                db[i] = Number(ten, "dolbyDb" + i, x + 1, 190, 46, -12, 12, false);
                db[i].AccessibleName = DolbyEqualizer.Labels[i] + " 精确值，dB";
                db[i].DecimalPlaces = 2; db[i].Increment = .1m; db[i].TextAlign = HorizontalAlignment.Center;
                sliders[i].ValueChanged += delegate {
                    if (loading) return;
                    db[slot].Value = sliders[slot].Value / 100m;
                };
                db[i].ValueChanged += delegate {
                    if (loading) return;
                    loading = true;
                    try {
                        sliders[slot].Value = (int)Math.Round(db[slot].Value * 100m);
                        var converted = DolbyEqualizer.ToTwenty(db.Select(n => n.Value).ToArray());
                        for (int j = 0; j < 20; j++) raw[j].Value = converted[j];
                    }
                    finally { loading = false; }
                };
            }
            for (int i = 0; i < 20; i++)
            {
                int x = i % 5 * 100, y = i / 5 * 60;
                LabelAt(twenty, "第 " + (i + 1) + " 段", x, y, 94, false);
                raw[i] = Number(twenty, "dolbyBand" + i, x, y + 24, 92, -192, 192, true);
                raw[i].AccessibleName = "均衡器第 " + (i + 1) + " 段，驱动原始值";
                raw[i].ValueChanged += delegate { if (!loading) RefreshTen(); };
            }
            var note = Palette.Label("范围 −12～+12 dB · 中线为 0 dB\n拖动或用方向键微调，数值框可精确输入。\n查看或切换视图不改曲线；修改后随方案保存。", 8F, Palette.Muted, false);
            note.SetBounds(0, 283, 500, 57); Controls.Add(note);
            mode.SelectedIndexChanged += delegate {
                ten.Visible = mode.SelectedIndex == 0; twenty.Visible = mode.SelectedIndex == 1;
                note.Text = mode.SelectedIndex == 0 ? "范围 −12～+12 dB · 中线为 0 dB\n拖动或用方向键微调，数值框可精确输入。\n查看或切换视图不改曲线；修改后随方案保存。" :
                    "20 段使用驱动原始值（−192～192），不是 dB。\n切回 10 点只近似显示原曲线，不重新生成中间值。\n修改 10 点才会重新换算整条曲线。";
            };
            mode.SelectedIndex = 0;
            ten.ResumeLayout(true); twenty.ResumeLayout(true); ResumeLayout(true);
        }
        private static void LabelAt(Control parent, string text, int x, int y, int width, bool centered)
        {
            var label = Palette.Label(text, 8F, Palette.Text, false); label.SetBounds(x, y, width, 24);
            if (centered) label.TextAlign = ContentAlignment.MiddleCenter;
            parent.Controls.Add(label);
        }
        private static NumberBox Number(Control parent, string name, int x, int y, int width, int min, int max, bool buttons)
        {
            var n = new NumberBox { Name = name, Minimum = min, Maximum = max, ShowButtons = buttons, Font = new Font("Microsoft YaHei UI", 8F), BackColor = Palette.Card, ForeColor = Palette.Text };
            n.SetBounds(x, y, width, 28); parent.Controls.Add(n); return n;
        }
        private void RefreshTen()
        {
            loading = true;
            try {
                var values = DolbyEqualizer.ToTen(Value());
                for (int i = 0; i < 10; i++) { db[i].Value = values[i]; sliders[i].Value = (int)Math.Round(values[i] * 100m); }
            }
            finally { loading = false; }
        }
        internal void LoadValues(int[] values)
        {
            values = values ?? new int[20]; DolbyEqualizer.ToTen(values);
            loading = true;
            try { for (int i = 0; i < 20; i++) raw[i].Value = values[i]; }
            finally { loading = false; }
            RefreshTen();
        }
        internal int[] Value() { return raw.Select(n => (int)n.Value).ToArray(); }
    }
}
