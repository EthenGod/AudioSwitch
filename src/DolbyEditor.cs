using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class DolbyEditor : ScrollCanvas
    {
        private readonly CheckBox use = new TickOption();
        private readonly ComboBox preset = new ChoiceBox();
        private readonly ComboBox[] toggles = new ComboBox[5];
        private readonly CheckBox[] strengths = new CheckBox[3];
        private readonly NumberBox[] numbers = new NumberBox[3];
        private readonly ComboBox ieq = new ChoiceBox();
        private readonly CheckBox useEq = new TickOption();
        private readonly DolbyEqualizerEditor equalizer = new DolbyEqualizerEditor();
        private readonly Panel content = new Panel();
        private readonly List<KeyValuePair<Control, Control>> alignedRows = new List<KeyValuePair<Control, Control>>();
        private bool aligningRows;
        private bool buildingRows = true;
        private readonly Label status;
        internal bool Busy { get; private set; }
        internal DolbyEditor(string deviceId, DolbyProfile profile, Func<Task<DolbyResult>> readCurrent = null)
        {
            BeginContentUpdate(); content.SuspendLayout();
            Dock = DockStyle.Fill; AutoScroll = false; BackColor = Palette.Card; ForeColor = Palette.Text;
            content.Layout += delegate { AlignRows(); };
            use.Name = "useDolby"; use.Text = "此设备成为声音输出时，自动应用 Dolby 方案"; use.SetBounds(12, 10, 506, 28); Canvas.Controls.Add(use);
            var help = Palette.Label("独立保存到此设备；Dolby 自定义槽位由驱动共享。\n可先在 Dolby Access 调好，再读取保存。未勾选的项目保持原值。", 8.5F, Palette.Muted, false);
            help.SetBounds(12, 43, 506, 44); Canvas.Controls.Add(help);
            var read = new FlatAction("读取当前 Dolby 并填入", false) { Name = "readDolby" }; read.SetBounds(12, 94, 204, 32); Canvas.Controls.Add(read);
            status = Palette.Label("读取需此设备是当前输出。设置失败只在面板提示，不弹窗。", 8F, Palette.Muted, false); status.SetBounds(12, 133, 506, 48); Canvas.Controls.Add(status);
            content.SetBounds(0, 188, 530, 820); Canvas.Controls.Add(content);
            var presetLabel = LabelAt("Dolby 预设", 12, 0, 130);
            Combo(preset, "dolbyPreset", 145, 0, 365, new[] { "保持当前预设", "电影", "自定义 1", "自定义 2" });
            AlignWith(presetLabel, preset);
            string[] labels = { "Dolby 总开关", "环绕虚拟化", "人声增强", "音量平衡", "Dolby 内容自动切换" };
            for (int i = 0; i < toggles.Length; i++) { var label = LabelAt(labels[i], 12, 40 + 35 * i, 180); toggles[i] = new ChoiceBox(); Combo(toggles[i], "dolbyToggle" + i, 238, 40 + 35 * i, 272, new[] { "保持不变", "开启", "关闭" }); AlignWith(label, toggles[i]); }
            for (int i = 0; i < 3; i++)
            {
                strengths[i] = new TickOption { Text = new[] { "环绕强度", "人声强度", "音量平衡强度" }[i] }; strengths[i].SetBounds(12, 224 + i * 34, 215, 28); content.Controls.Add(strengths[i]);
                numbers[i] = Number(238, 224 + i * 34, 105, 0, 100); var unit = LabelAt("%（驱动可能取整）", 352, 224 + i * 34, 176);
                AlignWith(strengths[i], numbers[i]); AlignWith(unit, numbers[i]);
                int slot = i; strengths[i].CheckedChanged += delegate { numbers[slot].Enabled = strengths[slot].Checked; };
            }
            var ieqLabel = LabelAt("智能均衡器", 12, 332, 180); Combo(ieq, "dolbyIeq", 238, 332, 272, new[] { "保持不变", "关闭", "平衡" }); AlignWith(ieqLabel, ieq);
            useEq.Text = "应用均衡器（自定义预设）"; useEq.Name = "dolbyUseEq"; useEq.SetBounds(12, 374, 495, 28); content.Controls.Add(useEq);
            equalizer.Location = new Point(12, 410); content.Controls.Add(equalizer);
            var foot = Palette.Label("内容自动切换开启后，Dolby 可能自行改变预设。\n低音增强暂不支持。", 8F, Palette.Muted, false); foot.SetBounds(12, equalizer.Bottom + 12, 515, 46); content.Controls.Add(foot);
            Action update = () => { use.Enabled = !Busy; read.Enabled = !Busy; content.Enabled = use.Checked && !Busy; bool custom = preset.SelectedIndex >= 2; ieq.Enabled = preset.SelectedIndex == 1; useEq.Enabled = custom; equalizer.Enabled = custom && useEq.Checked; };
            use.CheckedChanged += delegate { update(); }; preset.SelectedIndexChanged += delegate { update(); }; useEq.CheckedChanged += delegate { update(); };
            LoadProfile(profile); update();
            read.Click += async delegate {
                if (Busy) return;
                Busy = true; update(); status.Text = "正在读取…";
                try { var result = await (readCurrent == null ? Task.Run(() => DolbyWorker.Run(deviceId, null)) : readCurrent()); if (IsDisposed) return;
                    if (result == null || (result.Error == null && result.Profile == null)) throw new InvalidOperationException("Dolby 未返回有效方案，已保留原设置，请重试。");
                    if (result.Error != null) status.Text = result.Error;
                    else { DolbyProfiles.Validate(result.Profile); LoadProfile(result.Profile); status.Text = "已填入当前设置，点击“仅保存”或“保存并应用”后生效。"; }
                } catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
                finally { Busy = false; if (!IsDisposed) update(); }
            };
            buildingRows = false; content.ResumeLayout(true); AlignRows(); EndContentUpdate();
        }
        private Label LabelAt(string text, int x, int y, int width) { var label = Palette.Label(text, 9F, Palette.Text, false); label.TextAlign = ContentAlignment.MiddleLeft; label.SetBounds(x, y, width, 28); content.Controls.Add(label); return label; }
        private void AlignWith(Control caption, Control field)
        {
            if (caption is Label && !String.IsNullOrEmpty(field.Name)) caption.Name = field.Name + "Label";
            alignedRows.Add(new KeyValuePair<Control, Control>(caption, field)); AlignRows();
        }
        private void AlignRows()
        {
            if (aligningRows || buildingRows) return;
            aligningRows = true;
            try { foreach (var pair in alignedRows) pair.Key.SetBounds(pair.Key.Left, pair.Value.Top, pair.Key.Width, pair.Value.Height); }
            finally { aligningRows = false; }
        }
        private void Combo(ComboBox box, string name, int x, int y, int width, string[] items) { box.Name = name; box.DropDownStyle = ComboBoxStyle.DropDownList; box.FlatStyle = FlatStyle.Flat; box.BackColor = Palette.Card; box.ForeColor = Palette.Text; box.SetBounds(x, y, width, 28); box.Items.AddRange(items); box.SelectedIndex = 0; content.Controls.Add(box); }
        private NumberBox Number(int x, int y, int width, int min, int max) { var n = new NumberBox { Minimum = min, Maximum = max, BackColor = Palette.Card, ForeColor = Palette.Text }; n.SetBounds(x, y, width, 28); content.Controls.Add(n); return n; }
        private void LoadProfile(DolbyProfile p)
        {
            use.Checked = p != null; p = p ?? new DolbyProfile();
            preset.SelectedIndex = p.MainProfile == 0 ? 1 : p.MainProfile == 4 ? (p.SubProfile == 5 ? 3 : 2) : 0;
            var values = new[] { p.Enabled, p.Surround, p.Dialog, p.Leveler, p.AutoSwitch };
            for (int i = 0; i < values.Length; i++) toggles[i].SelectedIndex = !values[i].HasValue ? 0 : values[i].Value ? 1 : 2;
            var amounts = new[] { p.SurroundStrength, p.DialogStrength, p.LevelerStrength };
            for (int i = 0; i < amounts.Length; i++) { strengths[i].Checked = amounts[i].HasValue; numbers[i].Value = Math.Max(0, Math.Min(100, (decimal)(amounts[i] ?? 0) * 100)); numbers[i].Enabled = strengths[i].Checked; numbers[i].DecimalPlaces = 2; }
            ieq.SelectedIndex = !p.Ieq.HasValue ? 0 : p.Ieq == 0 ? 1 : 2;
            useEq.Checked = p.Eq != null;
            equalizer.LoadValues(p.Eq);
        }
        private bool? Toggle(int i) { return toggles[i].SelectedIndex == 0 ? (bool?)null : toggles[i].SelectedIndex == 1; }
        private double? Strength(int i) { return strengths[i].Checked ? (double?)numbers[i].Value / 100 : null; }
        internal DolbyProfile Value()
        {
            if (Busy) throw new InvalidOperationException("请等待读取 Dolby 设置完成。");
            if (!use.Checked) return null;
            var p = new DolbyProfile { MainProfile = preset.SelectedIndex == 0 ? (int?)null : preset.SelectedIndex == 1 ? 0 : 4,
                SubProfile = preset.SelectedIndex >= 2 ? (int?)(preset.SelectedIndex + 2) : null,
                Enabled = Toggle(0), Surround = Toggle(1), Dialog = Toggle(2), Leveler = Toggle(3), AutoSwitch = Toggle(4),
                SurroundStrength = Strength(0), DialogStrength = Strength(1), LevelerStrength = Strength(2),
                Ieq = ieq.Enabled && ieq.SelectedIndex > 0 ? (int?)(ieq.SelectedIndex == 1 ? 0 : 2) : null,
                Eq = useEq.Enabled && useEq.Checked ? equalizer.Value() : null };
            DolbyProfiles.Validate(p); return p;
        }
    }
}
