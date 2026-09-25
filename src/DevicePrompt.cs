using System;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    // This is the bottom-right actionable notification, hosted in its own short-lived process.
    internal sealed class DevicePrompt : Form
    {
        private readonly Func<Request, Task<Reply>> send;
        private readonly bool preview;
        private readonly Timer timer = new Timer { Interval = 300 };
        private readonly Panel body = new Panel();
        private readonly Label error;
        private readonly Label heading;
        private readonly Label counter;
        private readonly ToolTip tips = new ToolTip();
        private readonly Panel header;
        private readonly Panel footer;
        private Reply initial;
        private float uiScale = 1;
        private int bodyHeight = 138;
        private bool busy;
        private string signature;
        private string currentToken;
        private Process owner;

        internal DevicePrompt(int ownerPid = 0, Reply initial = null) : this(request => Task.Run(() => Wire.Send(request)), false)
        {
            this.initial = initial;
            if (ownerPid > 0)
            {
                Shown += delegate {
                    try
                    {
                        owner = Process.GetProcessById(ownerPid);
                        owner.EnableRaisingEvents = true;
                        owner.Exited += delegate { try { BeginInvoke((Action)(() => Close())); } catch (InvalidOperationException) { } };
                        if (owner.HasExited) Close();
                    }
                    catch (ArgumentException) { Close(); }
                };
            }
        }
        internal DevicePrompt(Func<Request, Task<Reply>> send, bool preview, Reply preload = null)
        {
            this.send = send; this.preview = preview;
            this.initial = preload;
            Text = "声间 · 设备切换提示"; Name = "DevicePrompt";
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = !preview;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Palette.Sidebar; ForeColor = Palette.Text;
            Shown += delegate { Palette.ChromeTree(this); };
            Font = new Font("Microsoft YaHei UI", 9F); ClientSize = new Size(360, 212);
            Padding = new Padding(1);
            header = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Palette.Card };
            heading = Palette.Label("设备变化", 10.5F, Palette.Text, true); heading.SetBounds(12, 10, 292, 25); header.Controls.Add(heading);
            var close = new FlatAction("×", false) { Name = "close", AccessibleName = "关闭提示，保留待处理选择", Location = new Point(323, 9), Size = new Size(24, 24) };
            close.Click += delegate { Close(); }; header.Controls.Add(close);
            body.Dock = DockStyle.Fill; body.AutoScroll = true;
            error = Palette.Label("", 8.5F, Palette.Warning, false); error.Dock = DockStyle.Bottom; error.Height = 44; error.Visible = false; error.Padding = new Padding(12, 2, 12, 2);
            footer = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            counter = Palette.Label("声间 · 手动选择", 8, Palette.Muted, false); counter.SetBounds(12, 4, 252, 20); footer.Controls.Add(counter);
            var manage = new FlatAction("打开面板", false) { Name = "manage", Location = new Point(277, 1), Size = new Size(70, 24) };
            manage.Click += async delegate { await Execute(new Request { Action = "show" }); }; footer.Controls.Add(manage);
            Controls.Add(body); Controls.Add(error); Controls.Add(footer); Controls.Add(header);
            timer.Tick += async delegate { await Execute(new Request { Action = "snapshot" }); };
            error.TextChanged += delegate { ResizePrompt(); };
            Shown += async delegate { PositionInCorner(); if (!preview) { if (this.initial == null) await Execute(new Request { Action = "snapshot" }); if (!IsDisposed) timer.Start(); } };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); tips.Dispose(); if (owner != null) owner.Dispose(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (var graphics = CreateGraphics()) uiScale = graphics.DpiX / 96F;
            if (initial != null)
            {
                error.Text = initial.Error ?? ""; error.Visible = initial.Error != null;
                if (initial.State != null) RenderReply(initial);
            }
            if (!IsDisposed) ResizePrompt();
        }
        private void Place(Control control, int x, int y, int width, int height)
        {
            control.SetBounds((int)(x * uiScale), (int)(y * uiScale), (int)(width * uiScale), (int)(height * uiScale));
        }
        private void ResizePrompt()
        {
            if (IsDisposed) return;
            ClientSize = new Size((int)(360 * uiScale), header.Height + footer.Height + (int)(bodyHeight * uiScale)
                + (String.IsNullOrEmpty(error.Text) ? 0 : error.Height) + 2);
            PositionInCorner();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Palette.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        private void PositionInCorner()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Height = Math.Min(Height, area.Height - 24);
            Width = Math.Min(Width, area.Width - 24);
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        }
        private async Task Execute(Request request)
        {
            if (busy || IsDisposed) return;
            busy = true;
            bool action = request.Action != "snapshot";
            if (action) body.Enabled = false;
            try
            {
                var reply = await send(request);
                if (IsDisposed) return;
                if (action || reply.Error != null) { error.Text = reply.Error ?? ""; error.Visible = reply.Error != null; }
                if (reply.State != null) RenderReply(reply);
                if (request.Action == "show" && reply.Error == null) Close();
            }
            catch (Exception ex)
            {
                if (!IsDisposed) { error.Text = "无法连接后台：" + ex.Message; error.Visible = true; }
            }
            finally { busy = false; if (!IsDisposed) body.Enabled = true; }
        }
        internal void RenderReply(Reply reply)
        {
            Palette.Apply(reply.Preferences.DarkMode);
            if (reply.Pending.Count == 0)
            {
                // Resolved by another window, a whitelist rule or unplugging: stale local errors
                // must not keep an otherwise completed notification alive.
                if (reply.Error == null) { Close(); return; }
                while (body.Controls.Count > 0) { var child = body.Controls[0]; body.Controls.RemoveAt(0); child.Dispose(); }
                heading.Text = "设备状态已变化";
                return;
            }
            var pending = reply.Pending.FirstOrDefault(p => p.Token == currentToken) ?? reply.Pending[0];
            string nextSignature = Wire.Encode(pending) + Wire.Encode(reply.State) + Wire.Encode(reply.Preferences) + reply.Pending.Count;
            if (signature == nextSignature) return;
            string selectedId = null;
            var previous = body.Controls.OfType<ComboBox>().FirstOrDefault();
            if (previous != null && previous.SelectedItem is Endpoint && currentToken == pending.Token)
                selectedId = ((Endpoint)previous.SelectedItem).Id;
            signature = nextSignature; currentToken = pending.Token;
            body.SuspendLayout();
            while (body.Controls.Count > 0) { var child = body.Controls[0]; body.Controls.RemoveAt(0); child.Dispose(); }
            bool lost = pending.IsDisconnection;
            string type = pending.Flow == 0 ? "输出" : "输入";
            heading.Text = lost ? "当前" + type + "设备已断开" : "发现新的" + type + "设备";
            heading.ForeColor = lost ? Palette.Warning : Palette.Accent;
            counter.Text = reply.Pending.Count > 1 ? "声间 · " + reply.Pending.Count + " 项待处理" : reply.Preferences.UseDevicePriority ? "声间 · 优先级已开启，可手动更改" : "声间 · 手动选择";
            string detail = lost ? "已断开：" + pending.PreviousName : "新设备：" + String.Join("、", pending.NewDevices.Select(d => d.Name));
            AddLabel(detail, 7, Palette.Text);
            var current = reply.State.Devices.FirstOrDefault(d => d.Id == reply.State.Default(pending.Flow, 1));
            AddLabel("当前：" + (current == null ? "暂无默认设备" : current.Name), 31, Palette.Muted);
            var devices = reply.State.Devices.Where(d => d.Flow == pending.Flow)
                .OrderByDescending(d => lost ? d.Id == reply.State.Default(pending.Flow, 1) : pending.NewDevices.Any(n => n.Id == d.Id))
                .ThenBy(d => d.Name).ToList();
            if (reply.Preferences.UseDevicePriority) devices = DevicePriority.Ordered(devices, reply.Preferences, reply.State, pending.Flow);
            int y = 59;
            if (devices.Count > 0)
            {
                var picker = new ChoiceBox { Name = "devices", AccessibleName = "选择在线" + type + "设备",
                    DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                    BackColor = Palette.Card, ForeColor = Palette.Text, DisplayMember = "Name", ValueMember = "Id",
                    DataSource = devices, DropDownWidth = (int)(460 * uiScale) };
                Place(picker, 12, y + 3, 240, 28);
                if (selectedId != null && devices.Any(d => d.Id == selectedId)) picker.SelectedValue = selectedId;
                body.Controls.Add(picker);
                var choose = new FlatAction("切换", true) { Name = "choose", AccessibleName = "切换到所选设备" };
                Place(choose, 260, y, 87, 30);
                Action updateText = delegate {
                    var selected = picker.SelectedItem as Endpoint;
                    choose.Text = selected != null && !lost && pending.NewDevices.Any(d => d.Id == selected.Id) ? "用新设备" : "切换";
                };
                picker.SelectedIndexChanged += delegate { updateText(); }; updateText();
                choose.Click += async delegate {
                    var selected = picker.SelectedItem as Endpoint;
                    if (selected != null) await Execute(new Request { Action = "alternative", Token = pending.Token, DeviceId = selected.Id });
                };
                body.Controls.Add(choose); y += 38;
            }
            else { AddLabel("暂无可用设备，请连接后再选择。", y, Palette.Muted); y += 28; }
            if (!lost)
            {
                var roles = reply.Preferences.IncludeCommunications ? new[] { 0, 1, 2 } : new[] { 0, 1 };
                var oldIds = roles.Select(r => { string id; return pending.PreviousDefaults.TryGetValue(pending.Flow + ":" + r, out id) ? id : null; }).Where(id => id != null).ToList();
                bool available = oldIds.Count > 0 && oldIds.All(id => devices.Any(d => d.Id == id));
                var keep = new FlatAction(available ? "继续用旧设备" : "旧设备不可用", false) { Name = "keepOld", Enabled = available };
                Place(keep, 12, y, 163, 30);
                tips.SetToolTip(keep, "恢复到接入前：" + pending.PreviousName);
                keep.Click += async delegate { await Execute(new Request { Action = "old", Token = pending.Token }); }; body.Controls.Add(keep);
            }
            var later = new FlatAction(devices.Count == 0 ? "知道了" : "保持当前选择", false) { Name = "keepSystem", AccessibleName = "保持系统当前选择，不更改设备设置" };
            Place(later, lost ? 12 : 183, y, lost ? 335 : 164, 30);
            later.Click += async delegate { await Execute(new Request { Action = "later", Token = pending.Token }); }; body.Controls.Add(later);
            bodyHeight = y + 40;
            body.ResumeLayout(true); ResizePrompt();
        }
        private void AddLabel(string text, int y, Color color)
        {
            var label = Palette.Label(text, 8.5F, color, false); Place(label, 12, y, 335, 22);
            tips.SetToolTip(label, text); body.Controls.Add(label);
        }
    }
}
