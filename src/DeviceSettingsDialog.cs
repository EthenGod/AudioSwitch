using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class DeviceSettingsDialog : Form
    {
        private readonly DeviceSettingsInfo info;
        private readonly Func<Request, Task<Reply>> send;
        private readonly CheckBox useVolume;
        private readonly LevelSlider volume;
        private readonly NumberBox number;
        private readonly ComboBox spatial;
        private readonly ComboBox rule;
        private readonly Label error;
        private readonly FlatAction save;
        private readonly FlatAction apply;
        private bool busy;
        private readonly DolbyEditor dolby;

        internal DeviceSettingsDialog(DeviceSettingsInfo info, Func<Request, Task<Reply>> send)
        {
            SuspendLayout();
            this.info = info; this.send = send;
            Text = "设备设置 · 声间"; BackColor = Palette.Background; ForeColor = Palette.Text;
            Shown += delegate { Palette.ChromeTree(this); };
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(530, 548); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var title = Palette.Label(info.Device.Name, 14, Palette.Text, true); title.SetBounds(24, 20, 480, 32); Controls.Add(title);
            var description = Palette.Label((info.Device.Flow == 0 ? "声音输出" : "麦克风输入") + "  /  独立预设 · 选择设备时自动应用", 9, Palette.Muted, false); description.SetBounds(24, 57, 480, 25); Controls.Add(description);
            useVolume = new TickOption { Name = "useVolume", Text = info.Device.Flow == 0 ? "应用此音量" : "应用此麦克风音量", Checked = info.Profile.Volume.HasValue,
                Location = new Point(24, 103), Size = new Size(220, 28), Enabled = info.CurrentVolume.HasValue };
            Controls.Add(useVolume);
            var live = Palette.Label(info.CurrentVolume.HasValue ? "当前：" + info.CurrentVolume.Value + "%" : "当前音量无法读取", 9, Palette.Muted, false);
            live.SetBounds(330, 106, 175, 25); live.TextAlign = ContentAlignment.MiddleRight; Controls.Add(live);
            int initial = Math.Max(0, Math.Min(100, info.Profile.Volume ?? info.CurrentVolume ?? 50));
            volume = new LevelSlider { Name = "volume", Minimum = 0, Maximum = 100, Value = initial, AccessibleName = "设备音量",
                Location = new Point(20, 144), Size = new Size(376, 38), BackColor = Palette.Background, Enabled = useVolume.Checked && useVolume.Enabled };
            number = new NumberBox { Name = "volumeNumber", Minimum = 0, Maximum = 100, Value = initial,
                Location = new Point(410, 145), Size = new Size(68, 29), BackColor = Palette.Card, ForeColor = Palette.Text, Enabled = volume.Enabled };
            Controls.Add(volume); Controls.Add(number);
            var percent = Palette.Label("%", 9, Palette.Muted, false); percent.SetBounds(484, 149, 24, 24); Controls.Add(percent);
            volume.ValueChanged += delegate { number.Value = volume.Value; };
            number.ValueChanged += delegate { volume.Value = (int)number.Value; };
            useVolume.CheckedChanged += delegate { volume.Enabled = number.Enabled = useVolume.Checked && useVolume.Enabled; };
            var spatialLabel = Palette.Label("空间音效", 10, Palette.Text, true); spatialLabel.SetBounds(24, 200, 200, 27); Controls.Add(spatialLabel);
            var options = new List<SpatialOption> { new SpatialOption { Id = null, Name = "保持设备当前音效" } };
            if (info.Device.Flow == 0 && info.Spatial != null) options.AddRange(info.Spatial.Options);
            if (info.Profile.SpatialFormat != null && !options.Any(o => o.Id != null && DeviceProfiles.SameFormat(o.Id, info.Profile.SpatialFormat)))
                options.Add(new SpatialOption { Id = info.Profile.SpatialFormat, Name = "已保存的音效（当前不可用）" });
            spatial = new ChoiceBox { Name = "spatial", DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", ValueMember = "Id",
                Location = new Point(24, 235), Size = new Size(480, 30), BindingContext = new BindingContext(), DataSource = options,
                Enabled = info.Device.Flow == 0 && info.Spatial != null, BackColor = Palette.Card, ForeColor = Palette.Text, FlatStyle = FlatStyle.Flat };
            spatial.SelectedIndex = options.FindIndex(o => info.Profile.SpatialFormat == null ? o.Id == null : o.Id != null && DeviceProfiles.SameFormat(o.Id, info.Profile.SpatialFormat));
            Controls.Add(spatial);
            string help = info.Device.Flow == 1 ? "麦克风不适用播放空间音效。" : info.SpatialError ??
                (info.Spatial != null && !info.Spatial.Supported ? "此设备不支持开启空间音效。" : "第三方音效需安装并激活；仅保存不会改变当前声音。");
            var hint = Palette.Label(help, 8.5F, Palette.Muted, false); hint.SetBounds(24, 271, 480, 37); Controls.Add(hint);
            var ruleLabel = Palette.Label("白名单 · 免打扰规则", 10, Palette.Text, true); ruleLabel.SetBounds(24, 313, 480, 27); Controls.Add(ruleLabel);
            rule = new ChoiceBox { Name = "deviceRule", AccessibleName = "设备白名单规则", DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(24, 349), Size = new Size(480, 30), BackColor = Palette.Card, ForeColor = Palette.Text, FlatStyle = FlatStyle.Flat };
            rule.Items.AddRange(new object[] { "不加入白名单（使用全局设置）", "接受系统选择，不弹窗", "接入时自动切换，不弹窗" });
            rule.SelectedIndex = Enum.IsDefined(typeof(DeviceRule), info.DeviceRule) ? (int)info.DeviceRule : 0;
            Controls.Add(rule);
            var ruleHint = Palette.Label("", 8.5F, Palette.Muted, false); ruleHint.SetBounds(24, 387, 480, 54); Controls.Add(ruleHint);
            Action updateHint = delegate {
                ruleHint.Text = rule.SelectedIndex == 1 ? "系统切到此设备时，保留系统选择和当前音量／音效，不弹出询问。" :
                    rule.SelectedIndex == 2 ? "检测到此设备接入后，自动切换并应用预设，不弹出询问。\n即使关闭全局优先级也生效；切换失败时仍提示原因。" :
                    "沿用全局优先级及接入询问设置。选择“仅保存”后，规则从下次设备变化开始生效。";
            };
            rule.SelectedIndexChanged += delegate { updateHint(); }; updateHint();
            error = Palette.Label(info.VolumeError ?? "", 8.5F, Palette.Warning, false); error.SetBounds(24, 449, 480, 43); Controls.Add(error);
            save = new FlatAction("仅保存", false) { Name = "save", Location = new Point(264, 495), Size = new Size(104, 34) };
            apply = new FlatAction("保存并应用", true) { Name = "apply", Location = new Point(380, 495), Size = new Size(124, 34) };
            save.Click += async delegate { await Save(false); }; apply.Click += async delegate { await Save(true); };
            Controls.Add(save); Controls.Add(apply);
            if (info.Device.Flow == 0)
            {
                ClientSize = new Size(592, 680);
                var tabs = new SectionTabs { Name = "settingsTabs", Location = new Point(16, 90), Size = new Size(560, 474) };
                var basic = new Panel() { BackColor = Palette.Card };
                var effects = new Panel() { BackColor = Palette.Card };
                foreach (Control control in Controls.Cast<Control>().Where(c => c.Top >= 100 && c.Top < 445).ToArray())
                { Controls.Remove(control); control.Top -= 90; basic.Controls.Add(control); }
                volume.BackColor = Palette.Card;
                foreach (int top in new[] { 100, 214 }) basic.Controls.Add(new Panel { BackColor = Palette.Border, Location = new Point(24, top), Size = new Size(480, 1) });
                dolby = new DolbyEditor(info.Device.Id, info.Profile.Dolby); effects.Controls.Add(dolby);
                tabs.AddSection("设备与规则", basic); tabs.AddSection("Dolby 方案", effects); Controls.Add(tabs);
                error.SetBounds(24, 576, 544, 48); save.Location = new Point(326, 633); apply.Location = new Point(442, 633);
                ruleHint.Text += "\n单独开启的 Dolby 方案仍随当前输出自动应用。";
                rule.SelectedIndexChanged += delegate { ruleHint.Text += "\n单独开启的 Dolby 方案仍随当前输出自动应用。"; };
            }
            var footerHint = Palette.Label("仅保存：留待下次使用\n保存并应用：立即切换到此设备", 8, Palette.Muted, false);
            footerHint.SetBounds(24, save.Top - 3, 224, 44); Controls.Add(footerHint);
            ResumeLayout(true);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !busy && (dolby == null || !dolby.Busy)) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private async Task Save(bool applyNow)
        {
            if (busy) return;
            busy = true; save.Enabled = apply.Enabled = false;
            try
            {
                var selected = spatial.SelectedItem as SpatialOption;
                var profile = new DeviceProfile { Volume = useVolume.Enabled ? (useVolume.Checked ? (int?)volume.Value : null) : info.Profile.Volume,
                    SpatialFormat = spatial.Enabled ? (selected == null ? null : selected.Id) : info.Profile.SpatialFormat,
                    Dolby = dolby == null ? null : dolby.Value() };
                var reply = await send(new Request { Action = "saveDeviceSettings", DeviceId = info.Device.Id, Profile = profile, Value = applyNow, DeviceRule = (DeviceRule)rule.SelectedIndex });
                if (IsDisposed) return;
                if (reply.Error != null) { error.Text = reply.Error; return; }
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { if (!IsDisposed) error.Text = ex.Message; }
            finally { busy = false; if (!IsDisposed) save.Enabled = apply.Enabled = true; }
        }
    }
}
