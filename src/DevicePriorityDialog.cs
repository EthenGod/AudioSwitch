// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal sealed class DevicePriorityDialog : Form
    {
        private readonly List<Endpoint> devices;
        private readonly HashSet<string> online;
        private readonly ListBox list;
        private readonly FlatAction up;
        private readonly FlatAction down;
        private readonly FlatAction save;
        private readonly Label error;
        private readonly int flow;
        private readonly Func<Request, Task<Reply>> send;
        private bool busy;

        internal DevicePriorityDialog(int flow, Reply reply, Func<Request, Task<Reply>> send)
        {
            this.flow = flow; this.send = send;
            var preferences = Wire.Decode<Preferences>(Wire.Encode(reply.Preferences));
            DevicePriority.Remember(preferences, reply.State);
            devices = preferences.DeviceOrder.Where(d => d.Flow == flow).ToList();
            online = new HashSet<string>(reply.State.Devices.Select(d => d.Id));
            Text = "设备优先级 · 声间"; BackColor = Palette.Background; ForeColor = Palette.Text;
            Shown += delegate { Palette.ChromeTree(this); };
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(550, 430); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var title = Palette.Label(flow == 0 ? "声音输出优先级" : "麦克风输入优先级", 16, Palette.Text, true);
            title.SetBounds(24, 20, 480, 34); Controls.Add(title);
            var hint = Palette.Label("越靠上越优先 · 离线设备保留位置，连接后继续按此顺序选择", 9, Palette.Muted, false);
            hint.SetBounds(24, 61, 505, 28); Controls.Add(hint);
            list = new PriorityList { Name = "priorityList", Location = new Point(24, 103), Size = new Size(402, 208),
                BackColor = Palette.Card, ForeColor = Palette.Text, BorderStyle = BorderStyle.None, IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 50, HorizontalScrollbar = false, AccessibleName = "设备优先级列表" };
            ((PriorityList)list).IsOnline = index => online.Contains(devices[index].Id);
            Controls.Add(list);
            up = new FlatAction("上移 ↑", false) { Name = "moveUp", Location = new Point(440, 103), Size = new Size(86, 36) };
            down = new FlatAction("下移 ↓", false) { Name = "moveDown", Location = new Point(440, 149), Size = new Size(86, 36) };
            up.Click += delegate { MoveSelected(-1); }; down.Click += delegate { MoveSelected(1); };
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(up); Controls.Add(down);
            var mode = Palette.Label(reply.Preferences.UseDevicePriority ? "已开启自动选择，保存后立即应用在线设备的最高优先级。" : "当前跟随系统选择；保存只调整排序，不会自动切换设备。", 8.5F, Palette.Muted, false);
            mode.SetBounds(24, 321, 502, 30); Controls.Add(mode);
            error = Palette.Label("", 8.5F, Palette.Warning, false); error.SetBounds(24, 352, 502, 28); Controls.Add(error);
            var cancel = new FlatAction("取消", false) { Name = "cancelOrder", Location = new Point(310, 383), Size = new Size(96, 32) };
            cancel.Click += delegate { Close(); }; Controls.Add(cancel);
            save = new FlatAction("保存排序", true) { Name = "saveOrder", Location = new Point(418, 383), Size = new Size(108, 32) };
            save.Click += async delegate { await Save(); }; Controls.Add(save);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            RefreshList(devices.Count == 0 ? -1 : 0);
        }
        private void RefreshList(int selected)
        {
            list.BeginUpdate(); list.Items.Clear();
            foreach (var device in devices) list.Items.Add(device.Name);
            list.SelectedIndex = selected; list.EndUpdate(); UpdateButtons();
        }
        private void UpdateButtons()
        {
            up.Enabled = !busy && list.SelectedIndex > 0;
            down.Enabled = !busy && list.SelectedIndex >= 0 && list.SelectedIndex < devices.Count - 1;
        }
        private void MoveSelected(int offset)
        {
            int from = list.SelectedIndex, to = from + offset;
            if (busy || from < 0 || to < 0 || to >= devices.Count) return;
            var device = devices[from]; devices.RemoveAt(from); devices.Insert(to, device); RefreshList(to);
        }
        private async Task Save()
        {
            if (busy) return;
            busy = true; save.Enabled = list.Enabled = false; UpdateButtons();
            try
            {
                var reply = await send(new Request { Action = "deviceOrder", Flow = flow, DeviceOrder = devices.Select(d => d.Id).ToList() });
                if (IsDisposed) return;
                if (reply.Error != null) { error.Text = reply.Error; return; }
                busy = false; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { if (!IsDisposed) error.Text = ex.Message; }
            finally { busy = false; if (!IsDisposed) { save.Enabled = list.Enabled = true; UpdateButtons(); } }
        }
    }
}
