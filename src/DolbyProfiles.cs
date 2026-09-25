// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    // null at either level means leave unchanged. EQ values are driver units, not dB.
    public sealed class DolbyProfile
    {
        public int? MainProfile { get; set; }
        public int? SubProfile { get; set; }
        public bool? Enabled { get; set; }
        public bool? Surround { get; set; }
        public bool? Dialog { get; set; }
        public bool? Leveler { get; set; }
        public bool? AutoSwitch { get; set; }
        public double? SurroundStrength { get; set; }
        public double? DialogStrength { get; set; }
        public double? LevelerStrength { get; set; }
        public int? Ieq { get; set; }
        public int[] Eq { get; set; }
    }
    internal interface IDolbyAccess
    {
        int Read(int op);
        double ReadStrength(int op);
        int[] ReadEq();
        void Write(int op, int value);
        void WriteStrength(int op, double value);
        void WriteEq(int[] value);
    }
    internal static class DolbyProfiles
    {
        internal static void Validate(DolbyProfile p)
        {
            if (p == null) return;
            if (p.MainProfile.HasValue && p.MainProfile != 0 && p.MainProfile != 4) throw new InvalidOperationException("目前支持电影和自定义 Dolby 预设。");
            if (p.MainProfile == 4 && !p.SubProfile.HasValue) throw new InvalidOperationException("请选择自定义预设槽位。");
            if (p.SubProfile.HasValue && (p.MainProfile != 4 || (p.SubProfile != 4 && p.SubProfile != 5))) throw new InvalidOperationException("请选择自定义 1 或自定义 2。");
            if (p.Eq != null && (p.MainProfile != 4 || !p.SubProfile.HasValue || p.Eq.Length != 20 || p.Eq.Any(v => v < -192 || v > 192))) throw new InvalidOperationException("均衡器需要自定义预设及 20 个 −192～192 的数值。");
            if (p.Ieq.HasValue && (p.MainProfile != 0 || (p.Ieq != 0 && p.Ieq != 2))) throw new InvalidOperationException("智能均衡器目前支持电影模式的关闭或平衡。");
            foreach (var value in new[] { p.SurroundStrength, p.DialogStrength, p.LevelerStrength })
                if (value.HasValue && (Double.IsNaN(value.Value) || Double.IsInfinity(value.Value) || value < 0 || value > 1)) throw new InvalidOperationException("Dolby 强度必须在 0–100% 之间。");
        }
        internal static void ValidateShape(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) throw new InvalidOperationException("Dolby 设置必须为对象。");
            foreach (var pair in map)
            {
                var property = typeof(DolbyProfile).GetProperty(pair.Key);
                if (property == null) throw new InvalidOperationException("未知 Dolby 设置字段。");
                if (pair.Value == null) continue;
                Type type = property.PropertyType;
                bool valid = type == typeof(bool?) ? pair.Value is bool : type == typeof(int?) ? pair.Value is int :
                    type == typeof(double?) ? pair.Value is int || pair.Value is decimal || pair.Value is double :
                    pair.Value is object[] && ((object[])pair.Value).All(x => x is int);
                if (!valid) throw new InvalidOperationException("Dolby 设置类型无效：" + pair.Key);
            }
        }
        internal static string EndpointGuid(string id)
        {
            Guid guid;
            if (id == null || !id.StartsWith("{0.0.0.00000000}.", StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(id.Substring(17), out guid))
                throw new InvalidOperationException("不是有效的 Windows 输出设备标识。");
            return guid.ToString("B").ToUpperInvariant();
        }
        internal static DolbyProfile Capture(IDolbyAccess a)
        {
            int main = a.Read(7); int? sub = main == 4 ? (int?)a.Read(38) : null;
            var p = new DolbyProfile { MainProfile = main, SubProfile = sub, Enabled = a.Read(5) != 0, Surround = a.Read(9) != 0,
                Dialog = a.Read(11) != 0, Leveler = a.Read(13) != 0, AutoSwitch = a.Read(73) != 0,
                SurroundStrength = a.ReadStrength(45), DialogStrength = a.ReadStrength(47), LevelerStrength = a.ReadStrength(49) };
            if (p.MainProfile == 4) p.Eq = a.ReadEq();
            if (p.MainProfile == 0) p.Ieq = a.Read(42);
            if (a.Read(7) != main || (sub.HasValue && a.Read(38) != sub.Value))
                throw new InvalidOperationException("读取期间 Dolby 预设发生变化，请重新读取。");
            Validate(p); return p;
        }
        private static bool ValidStrength(double value)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 1; }
        internal static string Apply(DolbyProfile p, IDolbyAccess a, Action guard, Action rollbackGuard = null)
        {
            Validate(p);
            if (p == null) return null;
            rollbackGuard = rollbackGuard ?? guard;
            var undo = new List<Action>(); var warnings = new List<string>();
            Action<int, int, int?> integer = (get, set, value) => {
                if (!value.HasValue) return;
                guard(); int before = a.Read(get); guard(); if (before == value.Value) return;
                undo.Add(() => { a.Write(set, before); if (a.Read(get) != before) throw new InvalidOperationException("Dolby 恢复未生效。"); }); a.Write(set, value.Value);
                if (a.Read(get) != value.Value) throw new InvalidOperationException("Dolby 未接受设置（" + set + "）。");
            };
            Action<int, int, double?> strength = (get, set, value) => {
                if (!value.HasValue) return;
                guard(); double before = a.ReadStrength(get); guard();
                if (!ValidStrength(before)) throw new InvalidOperationException("Dolby 返回无效的原始强度，已停止写入。");
                if (Math.Abs(before - value.Value) < 0.00001) return;
                undo.Add(() => { a.WriteStrength(set, before); double restored = a.ReadStrength(get); if (!ValidStrength(restored) || Math.Abs(restored - before) > 0.00001) throw new InvalidOperationException("Dolby 强度恢复未生效。"); }); a.WriteStrength(set, value.Value);
                double actual = a.ReadStrength(get);
                if (!ValidStrength(actual)) throw new InvalidOperationException("Dolby 返回无效强度。");
                if (Math.Abs(actual - value.Value) > 0.005) warnings.Add((get == 45 ? "环绕" : get == 47 ? "人声" : "音量平衡") + "强度按驱动档位调整为 " + Math.Round(actual * 100) + "%");
            };
            try
            {
                // Disable content switching before selecting a preset; enable it only after tuning.
                if (p.AutoSwitch == false) integer(73, 74, 0);
                // Switching a main profile also changes its active sub-profile: restore both on failure.
                if (p.MainProfile.HasValue)
                {
                    guard(); int main = a.Read(7);
                    if (main != p.MainProfile.Value)
                    {
                        int sub = main == 4 ? a.Read(38) : 0; guard();
                        undo.Add(() => { a.Write(8, main); if (main == 4 && a.Read(38) != sub) { rollbackGuard(); a.Write(39, sub); } if (a.Read(7) != main || (main == 4 && a.Read(38) != sub)) throw new InvalidOperationException("Dolby 预设恢复未生效。"); });
                        a.Write(8, p.MainProfile.Value);
                        if (a.Read(7) != p.MainProfile.Value) throw new InvalidOperationException("Dolby 未接受预设。");
                    }
                }
                integer(38, 39, p.SubProfile);
                integer(5, 6, p.Enabled.HasValue ? (int?)(p.Enabled.Value ? -1 : 0) : null);
                integer(9, 10, p.Surround.HasValue ? (int?)(p.Surround.Value ? 1 : 0) : null);
                integer(11, 12, p.Dialog.HasValue ? (int?)(p.Dialog.Value ? 1 : 0) : null);
                integer(13, 14, p.Leveler.HasValue ? (int?)(p.Leveler.Value ? 1 : 0) : null);
                integer(42, 43, p.Ieq);
                strength(45, 46, p.SurroundStrength); strength(47, 48, p.DialogStrength); strength(49, 50, p.LevelerStrength);
                if (p.Eq != null)
                {
                    guard(); int[] before = a.ReadEq(); guard();
                    if (before == null || before.Length != 20 || before.Any(n => n < -192 || n > 192)) throw new InvalidOperationException("Dolby 返回无效的原始均衡器，已停止写入。");
                    if (!before.SequenceEqual(p.Eq)) { undo.Add(() => { a.WriteEq(before); if (!a.ReadEq().SequenceEqual(before)) throw new InvalidOperationException("Dolby 均衡器恢复未生效。"); }); a.WriteEq(p.Eq); if (!a.ReadEq().SequenceEqual(p.Eq)) throw new InvalidOperationException("Dolby 未接受均衡器设置。"); }
                }
                if (p.AutoSwitch == true) integer(73, 74, -1);
                guard(); return warnings.Count == 0 ? null : String.Join("；", warnings);
            }
            catch (Exception ex)
            {
                bool incomplete = false;
                for (int i = undo.Count - 1; i >= 0; i--) { try { rollbackGuard(); undo[i](); } catch { incomplete = true; } }
                throw new InvalidOperationException(ex.Message + (incomplete ? " 部分 Dolby 更改未能恢复，请检查当前音效。" : undo.Count == 0 ? " 本次未写入 Dolby 设置。" : " 本次 Dolby 更改已恢复。"));
            }
        }
    }
}
