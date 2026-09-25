using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class Dashboard : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        private readonly Panel content = new ScrollSurface();
        private readonly Label status;
        private readonly Label summary;
        private readonly InlineNotice notice = new InlineNotice();
        private readonly FlatAction dismissNotice;
        private readonly Func<Request, Task<Reply>> send;
        private string dismissedError;
        private string dismissedWarning;
        private string shownError;
        private string shownWarning;
        private string noticeFailure;
        private string noticeWarning;
        private readonly CheckBox ask;
        private readonly CheckBox communications;
        private readonly CheckBox priority;
        private readonly CheckBox darkMode;
        private readonly Timer refreshTimer = new Timer { Interval = 1500 };
        private bool busy;
        private bool applying;
        private bool loading = true;
        private string lastSignature;
        private string[] previousTokens = new string[0];
        private Preferences currentPreferences = new Preferences();
        private Reply currentReply;
        private bool dolbyWasApplying;
        private readonly bool preview;
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 12000 };
        private readonly FlatAction[] filters = new FlatAction[3];
        private int selectedFlow = -1;
        private float uiScale = 1F;
        private bool resizingCards;

        internal Dashboard(bool prompt) : this(prompt, false) { }
        internal Dashboard(bool prompt, bool preview, Func<Request, Task<Reply>> send = null)
        {
            this.preview = preview;
            this.send = send ?? (request => Task.Run(() => Wire.Send(request)));
            Text = prompt ? "设备变化 · 音频设备管理" : "音频设备管理";
            Shown += delegate { Palette.ChromeTree(this); };
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Palette.Background; ForeColor = Palette.Text;
            ClientSize = new Size(1100, 740); MinimumSize = new Size(980, 680);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = AppIcon.Create();

            var sidebar = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = Palette.Sidebar, Padding = new Padding(20) };
            Controls.Add(sidebar);
            var logo = new BrandMark { Location = new Point(22, 30), Size = new Size(44, 44) }; sidebar.Controls.Add(logo);
            var brand = Palette.Label("声间", 19, Palette.Text, true); brand.SetBounds(79, 25, 120, 36); sidebar.Controls.Add(brand);
            var subtitle = Palette.Label("A U D I O  S W I T C H", 7, Palette.Muted, false); subtitle.SetBounds(81, 64, 136, 22); sidebar.Controls.Add(subtitle);
            var navigation = new Surface { Location = new Point(16, 116), Size = new Size(200, 46), Fill = Palette.Card, Stroke = Palette.Card };
            var nav = Palette.Label("声音设备", 10, Palette.Accent, true); nav.Dock = DockStyle.Fill; nav.Padding = new Padding(18, 0, 0, 0); nav.TextAlign = ContentAlignment.MiddleLeft; navigation.Controls.Add(nav); sidebar.Controls.Add(navigation);
            var preferences = Palette.Label("自动切换", 8.5F, Palette.Muted, true); preferences.SetBounds(24, 199, 170, 24); sidebar.Controls.Add(preferences);
            priority = CreateCheck("按设备优先级选择", 233); priority.Name = "usePriority"; sidebar.Controls.Add(priority);
            var priorityHint = Palette.Label("优先使用排序靠前的在线设备", 8, Palette.Muted, false); priorityHint.SetBounds(24, 267, 190, 24); sidebar.Controls.Add(priorityHint);
            ask = CreateCheck("新设备接入时询问", 308); sidebar.Controls.Add(ask);
            var askHint = Palette.Label("接入时提供快捷切换提示", 8, Palette.Muted, false); askHint.SetBounds(24, 342, 190, 24); sidebar.Controls.Add(askHint);
            communications = CreateCheck("同时切换通话设备", 383); sidebar.Controls.Add(communications);
            var callHint = Palette.Label("让通话与日常播放使用同一设备", 8, Palette.Muted, false); callHint.SetBounds(24, 417, 196, 24); sidebar.Controls.Add(callHint);
            darkMode = CreateCheck("深色模式", 466); darkMode.Name = "darkMode"; darkMode.Checked = Palette.Dark; sidebar.Controls.Add(darkMode);
            var export = new FlatAction("导出备份", false) { Name = "exportSettings", Location = new Point(22, 514), Size = new Size(90, 34) };
            var import = new FlatAction("导入设置", false) { Name = "importSettings", Location = new Point(120, 514), Size = new Size(90, 34) };
            export.Click += async delegate { await ExportSettings(); };
            import.Click += async delegate { await ImportSettings(); };
            sidebar.Controls.Add(export); sidebar.Controls.Add(import);
            var low = Palette.Label("关闭面板后，托盘继续工作", 8, Palette.Muted, false); low.SetBounds(24, ClientSize.Height - 65, 195, 24); low.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; sidebar.Controls.Add(low);
            var ver = Palette.Label("声间  /  Audio Switch", 8, Palette.Muted, false); ver.SetBounds(24, ClientSize.Height - 39, 190, 24); ver.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; sidebar.Controls.Add(ver);

            var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 26, 28, 18), BackColor = Palette.Background };
            Controls.Add(main); main.BringToFront();
            var header = new Panel { Dock = DockStyle.Top, Height = 143, Width = 812 };
            var title = Palette.Label("声音设备", 23, Palette.Text, true); title.SetBounds(0, 0, 530, 47); header.Controls.Add(title);
            summary = Palette.Label("正在连接音频后台…", 9, Palette.Muted, false); summary.SetBounds(2, 54, 560, 24); header.Controls.Add(summary);
            var reload = new FlatAction("刷新设备", false) { Size = new Size(96, 34), Location = new Point(716, 9), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            reload.Click += async delegate { await Execute(new Request { Action = "refresh" }); }; header.Controls.Add(reload);
            for (int i = 0; i < filters.Length; i++)
            {
                int flow = i - 1;
                var filter = new FlatAction(new[] { "全部设备", "声音输出", "麦克风输入" }[i], false) { Selected = i == 0, Name = "filter" + i, Location = new Point(i * 116, 94), Size = new Size(108, 34) };
                filter.Click += delegate {
                    selectedFlow = flow;
                    for (int j = 0; j < filters.Length; j++) { filters[j].Selected = j == flow + 1; filters[j].Invalidate(); }
                    lastSignature = null; if (currentReply != null) RenderReply(currentReply);
                };
                filters[i] = filter; header.Controls.Add(filter);
            }

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 42 };
            status = Palette.Label("正在连接…", 8.5F, Palette.Muted, false); status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; footer.Controls.Add(status);
            var hide = new FlatAction("收回托盘", false) { Dock = DockStyle.Right, Width = 100, Height = 35 };
            hide.Click += delegate { Close(); }; footer.Controls.Add(hide);
            notice.Dock = DockStyle.Bottom; notice.Visible = false;
            dismissNotice = notice.CloseButton;
            dismissNotice.Click += async delegate { await DismissNotice(); };
            content.Name = "deviceList"; content.Dock = DockStyle.Fill; content.AutoScroll = true;
            main.Controls.Add(content); main.Controls.Add(notice); main.Controls.Add(footer); main.Controls.Add(header);
            content.SizeChanged += delegate { ResizeCards(); };
            content.Layout += delegate { ResizeCards(); };
            ask.CheckedChanged += async delegate { if (!applying) await Execute(new Request { Action = "ask", Value = ask.Checked }); };
            communications.CheckedChanged += async delegate { if (!applying) await Execute(new Request { Action = "communications", Value = communications.Checked }); };
            priority.CheckedChanged += async delegate { if (!applying) await Execute(new Request { Action = "priority", Value = priority.Checked }); };
            darkMode.CheckedChanged += async delegate {
                if (applying) return;
                if (!busy) await Execute(new Request { Action = "darkMode", Value = darkMode.Checked });
                if (!IsDisposed) { applying = true; darkMode.Checked = currentPreferences.DarkMode; applying = false; }
            };
            refreshTimer.Tick += async delegate { await Execute(new Request { Action = "snapshot" }); };
            Shown += async delegate { if (!preview) { await Execute(new Request { Action = "snapshot" }); refreshTimer.Start(); } };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Close(); };
            FormClosed += delegate { refreshTimer.Stop(); refreshTimer.Dispose(); tips.Dispose(); Icon.Dispose(); };
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (var graphics = CreateGraphics()) uiScale = graphics.DpiX / 96F;
            Palette.WindowChrome(this);
        }
        private static CheckBox CreateCheck(string text, int y)
        {
            return new SwitchOption { Text = text, AccessibleName = text, ForeColor = Palette.Text, Location = new Point(24, y), Size = new Size(184, 32) };
        }
        private async Task Execute(Request request)
        {
            if (busy || IsDisposed || preview) return;
            busy = true;
            bool mutation = request.Action != "snapshot";
            if (mutation) { content.Enabled = false; ask.Enabled = false; communications.Enabled = false; priority.Enabled = false; darkMode.Enabled = false; }
            try
            {
                Reply reply = await send(request);
                if (IsDisposed) return;
                if (mutation && reply.Error != null) dismissedError = null;
                if (reply.State != null) RenderReply(reply);
                else RenderNotice(reply.Error, reply.Warning);
                if (mutation && reply.Error == null && !reply.DolbyApplying) status.Text = "●  已更新  ·  关闭窗口后，托盘继续为你工作";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) RenderNotice("暂时无法连接后台。请重新启动声间。  " + ex.Message, null);
            }
            finally
            {
                busy = false;
                if (!IsDisposed) { content.Enabled = true; ask.Enabled = true; communications.Enabled = true; priority.Enabled = true; darkMode.Enabled = true; }
            }
        }
        internal void RenderReply(Reply reply)
        {
            Palette.Apply(reply.Preferences.DarkMode);
            RenderNotice(reply.Error, reply.Warning);
            currentPreferences = reply.Preferences;
            currentReply = reply;
            if (reply.DolbyApplying || dolbyWasApplying) status.Text = reply.DolbyApplying ? "●  正在后台应用 Dolby 方案…" : "●  设备监听中  ·  关闭窗口即可释放界面";
            dolbyWasApplying = reply.DolbyApplying;
            applying = true; ask.Checked = reply.Preferences.AskOnConnect; communications.Checked = reply.Preferences.IncludeCommunications; priority.Checked = reply.Preferences.UseDevicePriority; darkMode.Checked = reply.Preferences.DarkMode; applying = false;
            string signature = Wire.Encode(reply.State) + Wire.Encode(reply.Pending) + Wire.Encode(reply.Preferences);
            if (signature == lastSignature) return;
            lastSignature = signature;
            bool newPrompt = reply.Pending.Any(p => !previousTokens.Contains(p.Token));
            previousTokens = reply.Pending.Select(p => p.Token).ToArray();
            summary.Text = StateSummary(reply.State) + (reply.Pending.Count > 0 ? "  ·  " + reply.Pending.Count + " 项设备变化待处理" : "  ·  选择设备，即刻切换");
            status.Text = reply.DolbyApplying ? "●  正在后台应用 Dolby 方案…" : "●  设备监听中  ·  关闭窗口即可释放界面";
            var scroll = content.AutoScrollPosition;
            content.SuspendLayout();
            tips.RemoveAll();
            while (content.Controls.Count > 0) { var child = content.Controls[0]; content.Controls.RemoveAt(0); child.Dispose(); }
            foreach (var pending in reply.Pending) content.Controls.Add(CreateArrival(pending, reply.State));
            for (int flow = 0; flow < 2; flow++) if (selectedFlow == -1 || selectedFlow == flow) content.Controls.Add(CreateGroup(flow, reply.State));
            foreach (Control card in content.Controls) if (uiScale != 1F) card.Scale(new SizeF(uiScale, uiScale));
            ResizeCards(); content.ResumeLayout(true);
            content.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            if ((loading || newPrompt) && reply.Pending.Count > 0) { content.ScrollControlIntoView(content.Controls[0]); }
            loading = false;
        }
        private static string StateSummary(AudioState state)
        {
            return state.Devices.Count(d => d.Flow == 0) + " 个输出  /  " + state.Devices.Count(d => d.Flow == 1) + " 个输入";
        }
        private Panel CreateArrival(Arrival pending, AudioState state)
        {
            bool disconnected = pending.IsDisconnection;
            var panel = new Surface { Width = 700, Fill = disconnected ? Palette.WarningBackground : Palette.Selected, Margin = new Padding(0, 0, 0, 18) };
            var title = Palette.Label(disconnected ? "当前" + (pending.Flow == 0 ? "输出设备" : "输入设备") + "已断开" : "新设备已就绪", 13,
                disconnected ? Palette.Warning : Palette.Accent, true);
            title.SetBounds(20, 16, 600, 29); panel.Controls.Add(title);
            var old = Palette.Label((disconnected ? "已断开：" : "接入前：") + pending.PreviousName, 9, Palette.Text, false);
            old.SetBounds(20, 53, 655, 26); old.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; panel.Controls.Add(old);
            var current = state.Devices.FirstOrDefault(d => d.Id == state.Default(pending.Flow, 1));
            var system = Palette.Label((currentPreferences.UseDevicePriority ? "当前选择（优先级已开启）：" : "系统当前选择：") + (current == null ? "暂无默认设备" : current.Name), 9, Palette.Muted, false);
            system.SetBounds(20, 82, 655, 26); system.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; panel.Controls.Add(system);
            int y = 119;
            foreach (var device in pending.NewDevices.Where(d => !disconnected))
            {
                var name = Palette.Label((pending.Flow == 0 ? "输出  /  " : "输入  /  ") + device.Name, 9, Palette.Text, true);
                name.SetBounds(20, y + 5, 430, 28); name.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; panel.Controls.Add(name);
                var button = new FlatAction("使用新设备", true) { Location = new Point(542, y), Size = new Size(140, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
                var captured = device;
                button.Click += async delegate { await Execute(new Request { Action = "new", Token = pending.Token, DeviceId = captured.Id }); }; panel.Controls.Add(button);
                y += 46;
            }
            var alternatives = DevicePriority.Ordered(state.Devices, currentPreferences, state, pending.Flow);
            if (alternatives.Count > 0)
            {
                var caption = Palette.Label(disconnected ? "选择可用设备" : "也可以选择其他在线设备", 8.5F, Palette.Muted, false);
                caption.SetBounds(20, y, 640, 25); caption.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; panel.Controls.Add(caption); y += 29;
                var selection = new ChoiceBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                    BackColor = Palette.Card, ForeColor = Palette.Text, DisplayMember = "Name", ValueMember = "Id",
                    Location = new Point(20, y + 3), Size = new Size(504, 32), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                    AccessibleName = "选择在线" + (pending.Flow == 0 ? "输出设备" : "输入设备"), DataSource = alternatives };
                panel.Controls.Add(selection);
                var choose = new FlatAction("使用所选设备", true) { Location = new Point(542, y), Size = new Size(140, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
                choose.Click += async delegate {
                    var selected = selection.SelectedItem as Endpoint;
                    if (selected != null) await Execute(new Request { Action = "alternative", Token = pending.Token, DeviceId = selected.Id });
                };
                panel.Controls.Add(choose); y += 51;
            }
            else
            {
                var empty = Palette.Label("暂无可用" + (pending.Flow == 0 ? "输出" : "输入") + "设备，请连接设备后再选择。", 9, Palette.Muted, false);
                empty.SetBounds(20, y, 640, 34); panel.Controls.Add(empty); y += 46;
            }
            int laterX = 20;
            if (!disconnected)
            {
                var keep = new FlatAction("继续用旧设备", false) { Location = new Point(20, y), Size = new Size(148, 34) };
                string oldId;
                keep.Enabled = pending.PreviousDefaults.TryGetValue(pending.Flow + ":1", out oldId) && state.Devices.Any(d => d.Id == oldId);
                if (!keep.Enabled) keep.Text = "旧设备不可用";
                keep.Click += async delegate { await Execute(new Request { Action = "old", Token = pending.Token }); }; panel.Controls.Add(keep);
                laterX = 180;
            }
            var later = new FlatAction(alternatives.Count == 0 ? "知道了" : currentPreferences.UseDevicePriority ? "保持当前选择" : "保持系统当前选择", false) { Location = new Point(laterX, y), Size = new Size(174, 34) };
            later.Click += async delegate { await Execute(new Request { Action = "later", Token = pending.Token }); }; panel.Controls.Add(later);
            var hint = Palette.Label("仅关闭提示，不更改设备设置", 8, Palette.Muted, false); hint.SetBounds(laterX + 190, y + 7, 300, 24); hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; panel.Controls.Add(hint);
            panel.Height = y + 54;
            return panel;
        }
        private Panel CreateGroup(int flow, AudioState state)
        {
            var devices = DevicePriority.Ordered(state.Devices, currentPreferences, state, flow);
            var panel = new Surface { Name = "deviceGroup" + flow, Width = 800, Margin = new Padding(0, 0, 0, 18), Height = 82 + Math.Max(1, devices.Count) * 94 };
            panel.SuspendLayout();
            var title = Palette.Label(flow == 0 ? "声音输出" : "麦克风输入", 13, Palette.Text, true); title.SetBounds(22, 18, 180, 28); panel.Controls.Add(title);
            var caption = Palette.Label(devices.Count + " 个在线  ·  " + (flow == 0 ? "耳机、扬声器与显示器" : "录音与语音通话"), 8.5F, Palette.Muted, false);
            caption.SetBounds(23, 48, 400, 24); panel.Controls.Add(caption);
            var sort = new FlatAction("调整优先级", false) { Name = "order" + flow, Location = new Point(670, 22), Size = new Size(108, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            sort.Click += delegate { EditPriority(flow); }; panel.Controls.Add(sort);
            int y = 80;
            if (devices.Count == 0)
            {
                var empty = Palette.Label(flow == 0 ? "还没有可用的声音输出" : "还没有可用的麦克风", 11, Palette.Text, true);
                empty.SetBounds(24, y + 9, 470, 28); panel.Controls.Add(empty);
                var help = Palette.Label("连接 USB、蓝牙或有线设备后，会自动显示在这里。", 9, Palette.Muted, false);
                help.SetBounds(24, y + 42, 590, 26); panel.Controls.Add(help);
            }
            foreach (var device in devices)
            {
                bool isDefault = state.Default(flow, 1) == device.Id;
                bool isCall = state.Default(flow, 2) == device.Id;
                var row = new Surface { Name = "deviceRow", Location = new Point(12, y), Size = new Size(776, 84), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Fill = isDefault ? Palette.Selected : Palette.Card, Stroke = isDefault ? Palette.SelectedBorder : Palette.Card };
                row.SuspendLayout();
                var glyph = new DeviceGlyph { Flow = flow, Active = isDefault, Location = new Point(14, 20), Size = new Size(44, 44), Anchor = AnchorStyles.Left, BackColor = row.Fill }; row.Controls.Add(glyph);
                var name = Palette.Label(device.Name, 10, Palette.Text, true); name.SetBounds(70, 12, 464, 25); name.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; row.Controls.Add(name);
                tips.SetToolTip(name, device.Name);
                var ordered = currentPreferences.DeviceOrder.Where(d => d.Flow == flow).Select(d => d.Id).ToList();
                int rank = ordered.IndexOf(device.Id) + 1;
                if (rank == 0) rank = devices.IndexOf(device) + 1;
                string detail = (isDefault ? "正在使用" : "已连接") + "  ·  优先级 " + rank;
                if (isCall) detail += "  ·  通话默认";
                var rule = DeviceAutomation.Rule(currentPreferences, device.Id);
                if (rule != DeviceRule.Normal) detail += rule == DeviceRule.AcceptSystem ? "  ·  接受系统免打扰" : "  ·  接入自动切换";
                var meta = Palette.Label(detail, 8, isDefault ? Palette.Accent : Palette.Muted, false); meta.SetBounds(70, 37, 464, 20);
                meta.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; tips.SetToolTip(meta, detail); row.Controls.Add(meta);
                bool fullySelected = isDefault && state.Default(flow, 0) == device.Id && (!communications.Checked || isCall);
                DeviceProfile profile;
                bool configured = currentPreferences.DeviceProfiles != null && currentPreferences.DeviceProfiles.TryGetValue(device.Id, out profile);
                profile = configured ? currentPreferences.DeviceProfiles[device.Id] : null;
                string preset = profile == null ? "未设置预设 · 保持设备原有声音" : (profile.Volume.HasValue ? "音量 " + profile.Volume + "%" : "音量保持") +
                    (flow == 1 ? "" : "  ·  " + (profile.SpatialFormat == null ? "音效保持" : DeviceProfiles.SameFormat(profile.SpatialFormat, "") ? "空间音效关闭" : "空间音效已保存"));
                if (profile != null && profile.Dolby != null) preset += "  ·  Dolby 自动方案";
                var presetLabel = Palette.Label(preset, 8, Palette.Muted, false); presetLabel.SetBounds(70, 59, 464, 20);
                presetLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; tips.SetToolTip(presetLabel, preset); row.Controls.Add(presetLabel);
                var action = new FlatAction(fullySelected ? (configured ? "应用预设" : "使用中") : "切换至此", !fullySelected) {
                    Name = "switchDevice", Location = new Point(548, 25), Size = new Size(112, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top, Enabled = !fullySelected || configured };
                action.AccessibleName = (fullySelected && configured ? "应用预设 " : "切换到 ") + device.Name;
                var captured = device;
                action.Click += async delegate { await Execute(new Request { Action = "switch", DeviceId = captured.Id }); };
                var settings = new FlatAction("设备设置", false) { Name = "deviceSettings", AccessibleName = "设备设置 " + device.Name,
                    Location = new Point(670, 25), Size = new Size(92, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
                settings.Click += async delegate { await EditDevice(captured.Id); };
                row.Controls.Add(action); row.Controls.Add(settings); row.ResumeLayout(true); panel.Controls.Add(row); y += 94;
            }
            panel.ResumeLayout(true);
            return panel;
        }
        private void ResizeCards()
        {
            if (resizingCards) return;
            resizingCards = true;
            try
            {
                int total = content.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Bottom);
                content.AutoScrollMinSize = new Size(0, total);
                int width = Math.Max((int)(620 * uiScale), content.ClientSize.Width - 2);
                int y = content.AutoScrollPosition.Y;
                foreach (Control card in content.Controls)
                {
                    card.SetBounds(0, y, width, card.Height);
                    y += card.Height + card.Margin.Bottom;
                }
            }
            finally { resizingCards = false; }
        }
        private async Task EditDevice(string id)
        {
            if (busy || preview) return;
            busy = true;
            try
            {
                var reply = await send(new Request { Action = "deviceSettings", DeviceId = id });
                if (IsDisposed) return;
                if (reply.Error != null || reply.DeviceSettings == null) throw new InvalidOperationException(reply.Error ?? "无法读取设备设置。");
                using (var editor = new DeviceSettingsDialog(reply.DeviceSettings, async request => {
                    var saved = await send(request);
                    if (!IsDisposed && saved.State != null) RenderReply(saved);
                    return saved;
                })) editor.ShowDialog(this);
            }
            catch (Exception ex) { if (!IsDisposed) { dismissedError = null; RenderNotice(ex.Message, null); } }
            finally { busy = false; }
        }
        private void EditPriority(int flow)
        {
            if (busy || preview || currentReply == null) return;
            busy = true;
            try
            {
                using (var editor = new DevicePriorityDialog(flow, currentReply, async request => {
                    var saved = await send(request);
                    if (!IsDisposed && saved.State != null) RenderReply(saved);
                    return saved;
                })) editor.ShowDialog(this);
            }
            finally { busy = false; }
        }
        private void RenderNotice(string failure, string warning)
        {
            noticeFailure = failure; noticeWarning = warning;
            if (failure == null) dismissedError = null;
            if (warning == null) dismissedWarning = null;
            shownError = failure != dismissedError ? failure : null;
            shownWarning = warning != dismissedWarning ? warning : null;
            notice.SetMessage(shownError ?? shownWarning, shownError != null);
        }
        private async Task ExportSettings()
        {
            if (busy || preview) return;
            busy = true;
            try
            {
                using (var dialog = new SaveFileDialog { Title = "导出声间设置备份", Filter = "声间配置 (*.json)|*.json", DefaultExt = "json", AddExtension = true,
                    FileName = "AudioSwitch-settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(PreferenceStore.SettingsPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("请选择其他备份位置，不要覆盖程序正在使用的配置文件。");
                    var reply = await send(new Request { Action = "exportSettings" });
                    if (reply.Error != null) throw new InvalidOperationException(reply.Error);
                    var settings = PreferenceStore.Parse(reply.ConfigurationJson);
                    PreferenceStore.Save(dialog.FileName, settings);
                    if (!IsDisposed) status.Text = "●  备份已导出：" + dialog.FileName;
                }
            }
            catch (Exception ex) { if (!IsDisposed) { dismissedError = null; RenderNotice("导出失败：" + ex.Message, null); } }
            finally { busy = false; }
        }
        private async Task ImportSettings()
        {
            if (busy || preview) return;
            busy = true;
            try
            {
                using (var dialog = new OpenFileDialog { Title = "导入声间设置备份", Filter = "声间配置 (*.json)|*.json", CheckFileExists = true, Multiselect = false })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string json = PreferenceStore.ReadFile(dialog.FileName);
                    var imported = PreferenceStore.Parse(json);
                    string summary = "将替换当前全部设置：\n" + imported.DeviceOrder.Count + " 个设备排序记录，" + imported.DeviceProfiles.Count + " 个设备预设，"
                        + imported.DeviceRules.Count(p => p.Value != DeviceRule.Normal) + " 条白名单。\n\n导入前会自动备份当前设置。离线设备记录会保留。\n不会立即切换设备或改变当前音量。\n\n确认导入？";
                    if (NoticeDialog.ShowNotice(this, "导入设置", summary, false, true) != DialogResult.OK) return;
                    var reply = await send(new Request { Action = "importSettings", ConfigurationJson = json });
                    if (IsDisposed) return;
                    if (reply.Error != null) throw new InvalidOperationException(reply.Error);
                    dismissedError = dismissedWarning = null;
                    if (reply.State != null) RenderReply(reply);
                    status.Text = "●  设置已导入，原配置已自动备份";
                    NoticeDialog.ShowNotice(this, "导入完成", "设置已导入。原配置备份：\n" + reply.BackupPath);
                }
            }
            catch (Exception ex) { if (!IsDisposed) { dismissedError = null; RenderNotice("导入失败：" + ex.Message, null); } }
            finally { busy = false; }
        }
        private async Task DismissNotice()
        {
            if (shownError != null)
            {
                dismissedError = shownError;
                RenderNotice(noticeFailure, noticeWarning);
                return;
            }
            string warning = shownWarning;
            if (warning == null) return;
            dismissedWarning = warning;
            RenderNotice(noticeFailure, noticeWarning);
            // This acknowledgement is independent of a snapshot already in flight.
            dismissNotice.Enabled = false;
            try
            {
                var reply = await send(new Request { Action = "dismissWarning", Message = warning });
                if (!IsDisposed)
                {
                    // Keep the local dismissal through stale responses until the next fresh snapshot.
                    if (reply.Error != null) status.Text = "●  提示已收起，后台同步未完成";
                }
            }
            catch { if (!IsDisposed) status.Text = "●  提示已收起，后台同步未完成"; }
            finally { if (!IsDisposed) dismissNotice.Enabled = true; }
        }
    }
}

