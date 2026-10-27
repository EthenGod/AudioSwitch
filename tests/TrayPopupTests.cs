using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;
using System.Windows.Forms;

namespace AudioSwitch
{
    internal static class TrayPopupTests
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        private static AudioState State(int count = 3)
        {
            var state = new AudioState();
            for (int i = 0; i < count; i++) state.Devices.Add(new Endpoint { Id = "output-" + i, Name = i == 2 ? "USB 音频设备 很长的中文设备名称，用于检查省略显示" : "同名耳机", Flow = 0 });
            state.Devices.Add(new Endpoint { Id = "mic", Name = "USB 麦克风", Flow = 1 });
            state.Defaults["0:1"] = "output-0"; state.Defaults["1:1"] = "mic";
            return state;
        }
        internal static void Run(Action<bool, string> check)
        {
            var requests = new List<Request>(); int opens = 0;
            var items = TrayMenuModel.Create(State(), new Preferences { GameMode = true }, false, 1, p => opens++, () => opens++, requests.Add, () => opens++);
            check(!items[1].Enabled && requests.Count == 0 && opens == 0, "tray snapshot never enumerates or writes audio and disables unavailable browser");
            var output = items.Single(i => i.Text == "声音输出"); var input = items.Single(i => i.Text == "麦克风输入");
            check(output.Children.Count == 3 && input.Children.Count == 1 && output.Children.Single(c => c.Checked).Text == "同名耳机", "tray separates input and output and preserves defaults");
            output.Children.Single(c => c.Text == "同名耳机" && !c.Checked).Execute();
            check(requests.Count == 1 && requests[0].Action == "switch" && requests[0].DeviceId == "output-1", "same-name tray devices select by endpoint ID");
            var game = items.Single(i => i.Text == "游戏模式"); game.Execute();
            check(game.Checked && requests.Last().Action == "gameMode" && !requests.Last().Value, "game mode uses confirmed snapshot and does not switch audio");
            var empty = TrayMenuModel.Create(new AudioState(), new Preferences(), true, 0, p => { }, () => { }, requests.Add, () => { });
            check(empty.Single(i => i.Text == "声音输出").Children.All(i => !i.Enabled), "empty tray device page has no clickable false device");
        }
        private static void Pump(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
        }
        private static void Raise(Control control, string name, EventArgs args)
        {
            control.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { args });
        }
        private static Color Pixel(Control control, int x, int y)
        {
            using (var image = new Bitmap(control.Width, control.Height)) { control.DrawToBitmap(image, control.ClientRectangle); return image.GetPixel(x, y); }
        }
        internal static void Render(Action<bool, string> check)
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"); Directory.CreateDirectory(directory);
            foreach (bool dark in new[] { false, true }) foreach (float scale in new[] { 1F, 1.5F, 2F }) {
                int commands = 0;
                var items = TrayMenuModel.Create(State(20), new Preferences { GameMode = false }, true, 0, p => commands++, () => commands++, r => commands++, () => commands++);
                var area = Screen.PrimaryScreen.WorkingArea;
                using (var popup = new TrayPopup(items, new Point(area.Right - 4, area.Bottom - 4), dark, scale, false)) {
                    check(!popup.IsHandleCreated, "popup styles and lifecycle are configured before first handle " + scale);
                    popup.Show(); Application.DoEvents();
                    string suffix = (dark ? "dark" : "light") + (scale == 1 ? "" : "-" + (int)(scale * 100));
                    check(area.Contains(popup.Bounds), "popup stays inside working area " + suffix);
                    check(!popup.ShowInTaskbar && !popup.Animating && commands == 0, "popup is transient and reduced motion has no timer " + suffix);
                    check(popup.Rows.All(r => r.Parent.ClientRectangle.Contains(r.Bounds)), "home rows remain inside popup " + suffix);
                    check(popup.Rows[0].PointToScreen(Point.Empty).Y - popup.Top <= 8 * scale + 1, "home starts with actions instead of an empty title band " + suffix);
                    var first = popup.Rows[0]; var palette = new TrayPopup.PopupTheme(dark);
                    first.Focus(); Raise(first, "OnMouseLeave", EventArgs.Empty);
                    check(Pixel(first, first.Width / 2, first.Height - (int)(5 * scale)).ToArgb() == palette.Card.ToArgb(), "automatic focus does not paint a stuck hover " + suffix);
                    Raise(first, "OnMouseEnter", EventArgs.Empty);
                    check(Pixel(first, first.Width / 2, first.Height - (int)(5 * scale)).ToArgb() == palette.Hover.ToArgb(), "pointer hover remains visible " + suffix);
                    Raise(first, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0));
                    Raise(first, "OnMouseLeave", EventArgs.Empty);
                    check(Pixel(first, first.Width / 2, first.Height - (int)(5 * scale)).ToArgb() == palette.Card.ToArgb(), "dragging out clears press and hover while focus remains " + suffix);
                    var pointerBorder = Pixel(first, first.Width / 2, 0);
                    popup.HandleNavigation(Keys.Home);
                    check(first.Focused && Pixel(first, first.Width / 2, 0).ToArgb() != pointerBorder.ToArgb(), "keyboard navigation shows an explicit focus ring " + suffix);
                    Raise(first, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 12, 12, 0)); Raise(first, "OnMouseLeave", EventArgs.Empty);
                    using (var bitmap = new Bitmap(popup.Width, popup.Height)) { popup.DrawToBitmap(bitmap, popup.ClientRectangle); bitmap.Save(Path.Combine(directory, "tray-popup-" + suffix + ".png")); }
                    if (dark && scale == 1) {
                        popup.HandleNavigation(Keys.Home); popup.HandleNavigation(Keys.Right);
                        check(popup.Rows[1].Focused, "right moves between the two top actions");
                        popup.HandleNavigation(Keys.Right);
                        check(popup.Rows[1].Focused, "right edge does not wrap or open another action");
                        popup.HandleNavigation(Keys.Down);
                        check(popup.Rows[2].Focused, "down from browser goes to the next visual row");
                        popup.HandleNavigation(Keys.Up);
                        check(popup.Rows[1].Focused, "up restores the chosen column of the top row");
                        popup.HandleNavigation(Keys.Left);
                        check(popup.Rows[0].Focused, "left returns to the main panel action");
                        popup.HandleNavigation(Keys.Down);
                        check(popup.Rows[2].Focused, "down from panel skips its horizontal neighbor");
                        popup.HandleNavigation(Keys.Right);
                        check(popup.Rows.Count == 20, "right still opens a device submenu");
                        popup.HandleNavigation(Keys.Left);
                        check(popup.Rows[2].Focused && commands == 0, "left from a submenu returns to its source without executing commands");
                    }
                    var group = items.Single(i => i.Text == "声音输出"); popup.Navigate(group); Application.DoEvents();
                    check(popup.Rows.Count == 20 && area.Contains(popup.Bounds), "long device page scrolls inside same window " + suffix);
                    check(popup.Rows[0].AccessibilityObject.State.HasFlag(AccessibleStates.Checked), "selected device remains accessible " + suffix);
                    check(popup.Controls.OfType<Button>().Any(b => b.Visible && b.AccessibleName == "返回快捷菜单"), "device page has accessible back button " + suffix);
                    using (var bitmap = new Bitmap(popup.Width, popup.Height)) { popup.DrawToBitmap(bitmap, popup.ClientRectangle); bitmap.Save(Path.Combine(directory, "tray-popup-devices-" + suffix + ".png")); }
                    var backButton = popup.Controls.OfType<Button>().Single(b => b.Visible && b.AccessibleName == "返回快捷菜单");
                    Raise(backButton, "OnMouseEnter", EventArgs.Empty);
                    check(Pixel(backButton, 0, 0).ToArgb() == palette.Card.ToArgb() && Pixel(backButton, backButton.Width / 2, backButton.Height - (int)(5 * scale)).ToArgb() == palette.Hover.ToArgb(), "back uses rounded shared hover paint " + suffix);
                    using (var bitmap = new Bitmap(popup.Width, popup.Height)) { popup.DrawToBitmap(bitmap, popup.ClientRectangle); bitmap.Save(Path.Combine(directory, "tray-popup-back-hover-" + suffix + ".png")); }
                    Raise(backButton, "OnMouseLeave", EventArgs.Empty); backButton.Focus(); popup.HandleNavigation(Keys.Enter);
                    check(popup.Rows.Count == items.Count && popup.Visible && commands == 0, "Enter on back returns without executing a backend command " + suffix);
                    popup.Navigate(group);
                    popup.Controls.OfType<Button>().Single(b => b.Visible && b.AccessibleName == "返回快捷菜单").Focus();
                    popup.HandleNavigation(Keys.Up); Application.DoEvents();
                    var last = popup.Rows.Last();
                    check(last.Focused && last.Parent.PointToScreen(last.Location).Y >= popup.Top && last.Parent.PointToScreen(new Point(0, last.Bottom)).Y <= popup.Bottom, "up from back button reveals the last device " + suffix);
                    popup.HandleNavigation(Keys.Left); check(popup.Rows.Count == items.Count && commands == 0, "keyboard back never triggers commands " + suffix);
                    var game = popup.Rows.Single(r => r.Command.Text == "游戏模式");
                    check(game.AccessibilityObject.Role == AccessibleRole.CheckButton, "game mode exposes check semantics " + suffix);
                    popup.ActivateCommand(items[0]);
                    check(!popup.Visible && popup.Chosen != null && commands == 0, "command closes UI before backend work " + suffix);
                    popup.Chosen(); check(commands == 1, "chosen command delivered once " + suffix);
                }
            }
            var entries = TrayMenuModel.Create(State(), new Preferences(), true, 0, p => { }, () => { }, r => { }, () => { });
            var unavailableBrowser = TrayMenuModel.Create(State(), new Preferences(), false, 0, p => { }, () => { }, r => { }, () => { });
            using (var popup = new TrayPopup(unavailableBrowser, new Point(400, 500), true, 1, false)) {
                popup.Show(); popup.HandleNavigation(Keys.Home); popup.HandleNavigation(Keys.Right);
                check(popup.Rows[0].Focused, "horizontal navigation skips an unavailable browser action");
                popup.HandleNavigation(Keys.Down); popup.HandleNavigation(Keys.Up);
                check(popup.Rows[0].Focused, "vertical return selects the enabled top action");
                popup.Close();
            }
            using (var popup = new TrayPopup(entries, new Point(400, 500), true, 1, true)) {
                popup.Show(); Pump(300); check(!popup.Animating && popup.Opacity == 1, "popup entrance settles and stops timer");
                popup.Rows[2].Focus(); popup.Rows[3].Focus(); Pump(200);
                check(!popup.Animating, "rapid focus changes settle without idle animation");
                popup.Navigate(entries.Single(i => i.Text == "声音输出")); Pump(60);
                popup.GoBack(); Pump(300);
                check(!popup.Animating && popup.Rows.Count == entries.Count && popup.Rows[0].Parent.Parent.Left == 6, "page animation reverses and settles at the shared inset");
                var row = popup.Rows[0]; row.Focus();
                Raise(row, "OnMouseEnter", EventArgs.Empty); Pump(60);
                Raise(row, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0));
                Raise(row, "OnMouseCaptureChanged", EventArgs.Empty); Raise(row, "OnMouseLeave", EventArgs.Empty); Pump(180);
                check(!popup.Animating && Pixel(row, row.Width / 2, row.Height - 5).ToArgb() == new TrayPopup.PopupTheme(true).Card.ToArgb(), "press cancellation and hover release settle without stuck color or timer");
                popup.Dismiss(); Pump(240); check(!popup.Visible && !popup.Animating, "dismissal completes and stops timer");
            }
            // A non-interactive sandbox has no foreground window; report that boundary explicitly.
            if (GetForegroundWindow() == IntPtr.Zero) Console.WriteLine("SKIP foreground checks: run --tray-popup-render on the interactive desktop");
            else using (var cover = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(240, 200, 400, 380) }) {
                cover.Show(); cover.Activate(); Pump(40);
                using (var popup = new TrayPopup(entries, new Point(600, 570), true, 1, true)) {
                    popup.Show(); Pump(300);
                    check(popup.Visible && GetForegroundWindow() == popup.Handle, "first popup takes foreground above an existing topmost flyout");
                    cover.Activate(); Pump(240);
                    check(!popup.Visible, "a later focus change still dismisses the popup");
                }
                Exception failure = null;
                using (var host = new TrayPopupHost(action => action(), ex => failure = ex)) {
                    // Reproduce a second right click while the first STA is still starting.
                    host.Show(new Point(600, 570), entries, true);
                    host.Show(new Point(600, 570), entries, true);
                    Pump(600);
                    var foreground = GetForegroundWindow(); var title = new StringBuilder(128);
                    GetWindowText(foreground, title, title.Capacity);
                    check(host.Running && failure == null && IsWindowVisible(foreground) && title.ToString() == "声间 快捷菜单", "rapid right clicks preserve the latest open request on the separate STA");
                    host.Dispose();
                    Pump(100);
                    check(!host.Running, "replacement popup releases its STA after disposal");
                }
                cover.Close();
            }
            for (int i = 0; i < 3; i++) {
                Exception failure = null;
                var host = new TrayPopupHost(action => action(), ex => failure = ex);
                host.Show(new Point(400, 500), entries, true);
                if (i == 0) Thread.Sleep(200); // The backend thread deliberately does not pump messages.
                host.Dispose();
                var watch = Stopwatch.StartNew();
                while (host.Running && watch.ElapsedMilliseconds < 2000) Thread.Sleep(10);
                check(!host.Running && failure == null, "separate popup UI exits after normal or immediate disposal " + i);
            }
        }
    }
}
