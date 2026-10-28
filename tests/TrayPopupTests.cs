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
        [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
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
            var typed = State(); typed.FormFactors["output-0"] = 1; typed.FormFactors["output-1"] = 3; typed.FormFactors["output-2"] = 9; typed.FormFactors["mic"] = 5;
            check(TrayMenuModel.DeviceGlyph(typed.Devices[0], typed) == TrayGlyph.Speaker && TrayMenuModel.DeviceGlyph(typed.Devices[1], typed) == TrayGlyph.Output && TrayMenuModel.DeviceGlyph(typed.Devices[2], typed) == TrayGlyph.Monitor, "same-name endpoints use Windows form factors by ID");
            check(TrayMenuModel.DeviceGlyph(typed.Devices.Last(), typed) == TrayGlyph.Microphone, "headset input retains microphone icon");
            typed.FormFactors.Clear();
            check(TrayMenuModel.DeviceGlyph(typed.Devices[0], typed) == TrayGlyph.Output, "missing type metadata falls back to a recognizable headphone name");
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
        private static void ContinuousInteraction(Action<bool, string> check, string directory)
        {
            var state = State(20); var preferences = new Preferences { DeviceOrder = State(20).Devices };
            int submitted = 0; TrayCommand accepted = null; Action<IList<TrayCommand>, string> finish = null;
            Action<Request> apply = request => {
                if (request.Action == "switch") state.Defaults["0:1"] = request.DeviceId;
                if (request.Action == "gameMode") preferences.GameMode = request.Value;
                if (request.Action == "priority") preferences.UseDevicePriority = request.Value;
            };
            Func<IList<TrayCommand>> snapshot = () => TrayMenuModel.Create(state, preferences, true, 0, p => { }, () => { }, apply, () => { });
            using (var popup = new TrayPopup(snapshot(), new Point(700, 700), true, 1, false, (command, done) => { submitted++; accepted = command; finish = done; })) {
                popup.Show(); popup.Navigate(popup.Rows.Single(r => r.Command.Key == "flow:0").Command);
                popup.HandleNavigation(Keys.End);
                var selected = popup.Rows.Last(); string id = selected.Command.Key; var oldRow = popup.Rows[0];
                int offset = selected.Parent.Top; var window = popup.Handle;
                popup.ActivateCommand(selected.Command); popup.ActivateCommand(selected.Command);
                check(submitted == 1 && popup.Busy && popup.Visible && popup.Chosen == null && !selected.Command.Checked, "continuous selection stays open and rejects duplicate pending submissions without optimistic checks");
                check(selected.Enabled && selected.Focused && selected.AccessibilityObject.State.HasFlag(AccessibleStates.Unavailable), "pending selection keeps its appearance and focus while exposing the temporary action lock");
                accepted.Execute(); finish(snapshot(), null); Pump(40);
                check(!popup.Busy && popup.Visible && popup.Handle == window && Object.ReferenceEquals(oldRow, popup.Rows[0]) && selected.Parent.Top == offset, "confirmed selection reuses window and rows without losing scroll position");
                check(popup.Rows.Single(r => r.Command.Checked).Command.Key == id && popup.Rows.Single(r => r.Command.Key == "device:output-0").Command.Checked == false, "confirmed selection updates by endpoint ID rather than duplicate names");
                popup.ActivateCommand(popup.Rows[0].Command); finish(snapshot(), "设备暂时不可用，请重试。"); Pump(40);
                check(popup.Visible && !popup.Busy && popup.Rows.Single(r => r.Command.Checked).Command.Key == id && popup.Controls.OfType<Label>().Any(l => l.Visible && l.Text.Contains("重试")), "failure retains confirmed selection and shows retry guidance in the open menu");
                using (var image = new Bitmap(popup.Width, popup.Height)) { popup.DrawToBitmap(image, popup.ClientRectangle); image.Save(Path.Combine(directory, "tray-popup-operation-error.png")); }
                popup.GoBack(); var game = popup.Rows.Single(r => r.Command.Key == "gameMode");
                game.PerformClick(); accepted.Execute(); finish(snapshot(), null); Pump(40);
                check(preferences.GameMode && game.Command.Checked && popup.Visible && !popup.Controls.OfType<Label>().Any(l => l.Visible), "toggle stays open and clears the previous error after confirmed success");
                game.PerformClick(); accepted.Execute(); finish(snapshot(), null); Pump(40);
                check(!preferences.GameMode && !game.Command.Checked, "second toggle uses the refreshed command rather than a stale captured value");
                popup.Navigate(popup.Rows.Single(r => r.Command.Key == "flow:0").Command);
                state.Devices.RemoveAll(d => d.Flow == 0); popup.UpdateSnapshot(snapshot());
                check(popup.Visible && popup.Rows.Count == 1 && !popup.Rows[0].Enabled, "external device removal updates the retained submenu without fake choices");
                popup.GoBack(); popup.ActivateCommand(popup.Rows.Single(r => r.Command.Key == "gameMode").Command);
                popup.Close(); finish(snapshot(), null); Pump(40);
                check(!popup.Visible && !popup.Animating, "late reply after dismissal does not recreate a window or timer");
            }
        }
        private static void SelectionMotion(Action<bool, string> check)
        {
            var state = State(); var preferences = new Preferences { DeviceOrder = State().Devices };
            Func<IList<TrayCommand>> snapshot = () => TrayMenuModel.Create(state, preferences, true, 0, p => { }, () => { }, r => { }, () => { });
            using (var popup = new TrayPopup(snapshot(), new Point(700, 700), true, 1, true)) {
                popup.Show(); popup.Navigate(popup.Rows.Single(r => r.Command.Key == "flow:0").Command); Pump(320);
                var row = popup.Rows.Single(r => r.Command.Key == "device:output-1");
                Raise(row, "OnMouseLeave", EventArgs.Empty); Pump(180);
                var theme = new TrayPopup.PopupTheme(true);
                state.Defaults["0:1"] = "output-1"; popup.UpdateSnapshot(snapshot()); Pump(35);
                var intermediate = Pixel(row, row.Width / 2, row.Height - 5);
                check(row.Command.Checked && intermediate.ToArgb() != theme.Card.ToArgb() && intermediate.ToArgb() != theme.Selected.ToArgb(), "confirmed device highlight passes through an intermediate color");
                Pump(180);
                check(Pixel(row, row.Width / 2, row.Height - 5).ToArgb() == theme.Selected.ToArgb() && !popup.Animating, "confirmed device highlight settles and stops animation");
                Raise(row, "OnMouseEnter", EventArgs.Empty); Pump(180);
                check(Pixel(row, row.Width / 2, row.Height - 5).ToArgb() == theme.Selected.ToArgb(), "hover does not erase the current device highlight");
                popup.Close();
            }
        }
        internal static void Render(Action<bool, string> check)
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"); Directory.CreateDirectory(directory);
            ContinuousInteraction(check, directory);
            SelectionMotion(check);
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
                uint key, flags; byte alpha;
                var handle = popup.Handle; Pump(60);
                check(GetLayeredWindowAttributes(handle, out key, out alpha, out flags) && alpha == 0 && !popup.Animating, "first frame is transparent and no hover timer starts before the popup is shown");
                popup.Show(); byte previous = 0; bool monotonic = true;
                for (int frame = 0; frame < 18; frame++) { Pump(20); GetLayeredWindowAttributes(handle, out key, out alpha, out flags); monotonic &= alpha >= previous; previous = alpha; }
                check(monotonic && alpha == 255 && (GetWindowLong(handle, -20) & 0x80000) != 0, "entrance alpha increases to opaque without changing the layered window style");
                check(!popup.Animating && popup.PresentationOpacity == 1, "popup entrance settles and stops timer");
                popup.Rows[2].Focus(); popup.Rows[3].Focus(); Pump(200);
                check(!popup.Animating, "rapid focus changes settle without idle animation");
                popup.Navigate(entries.Single(i => i.Text == "声音输出"));
                var enteringRow = popup.Rows[0];
                int firstPageX = enteringRow.Parent.Left;
                var firstPageColor = Pixel(enteringRow, enteringRow.Width / 2, enteringRow.Height / 2);
                Pump(90);
                int middlePageX = enteringRow.Parent.Left;
                var middlePageColor = Pixel(enteringRow, enteringRow.Width / 2, enteringRow.Height / 2);
                Pump(200);
                var finalPageColor = Pixel(enteringRow, enteringRow.Width / 2, enteringRow.Height / 2);
                check(firstPageColor.ToArgb() != middlePageColor.ToArgb() && middlePageColor.ToArgb() != finalPageColor.ToArgb(), "submenu has distinct initial, intermediate and final painted frames");
                check(firstPageX > middlePageX && middlePageX > 0 && enteringRow.Parent.Left == 0, "submenu content slides left from the right edge and settles without moving the viewport");
                popup.GoBack(); int returnPageX = popup.Rows[0].Parent.Left; Pump(80);
                check(returnPageX < 0 && popup.Rows[0].Parent.Left > returnPageX && popup.Rows[0].Parent.Left < 0, "home content slides right when returning from the submenu");
                Pump(220);
                check(!popup.Animating && popup.Rows.Count == entries.Count && popup.Rows[0].Parent.Parent.Left == 6, "page animation reverses and settles at the shared inset");
                popup.Navigate(entries.Single(i => i.Text == "麦克风输入")); Pump(30); popup.GoBack(); Pump(300);
                check(popup.Rows.Last().Command.Key == "exit" && popup.Rows.Last().Visible && !popup.Animating, "return from a shorter submenu restores the complete home page");
                var row = popup.Rows[0]; row.Focus();
                Raise(row, "OnMouseEnter", EventArgs.Empty); Pump(60);
                Raise(row, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0));
                Raise(row, "OnMouseCaptureChanged", EventArgs.Empty); Raise(row, "OnMouseLeave", EventArgs.Empty); Pump(180);
                check(!popup.Animating && Pixel(row, row.Width / 2, row.Height - 5).ToArgb() == new TrayPopup.PopupTheme(true).Card.ToArgb(), "press cancellation and hover release settle without stuck color or timer");
                popup.Dismiss(); Pump(60); double fading = popup.PresentationOpacity; popup.Reopen();
                check(popup.Handle == handle && popup.PresentationOpacity == fading, "reopening during dismissal continues from current opacity on the same window");
                Pump(300); popup.Dismiss(); Pump(240); check(!popup.Visible && !popup.Animating, "dismissal completes and stops timer");
            }
            for (int entry = 0; entry < 2; entry++) {
                int opened = 0;
                var actions = TrayMenuModel.Create(State(), new Preferences(), true, 0, p => opened++, () => opened++, r => { }, () => { });
                using (var popup = new TrayPopup(actions, new Point(600, 570), true, 1, true)) {
                    popup.Show(); Pump(300); popup.ActivateCommand(actions[entry]); Pump(60);
                    check(popup.Visible && popup.Closing && popup.PresentationOpacity > 0 && popup.PresentationOpacity < 1 && popup.Rows[entry].Enabled && opened == 0, "top action fades without dimming or launching another surface early " + entry);
                    popup.ActivateCommand(actions[entry]); Pump(200);
                    check(!popup.Visible && !popup.Animating && popup.Chosen != null && opened == 0, "top action completes its exit before handing off " + entry);
                    popup.Chosen(); check(opened == 1, "top action remains a single launch " + entry);
                }
            }
            // A non-interactive sandbox has no foreground window; report that boundary explicitly.
            if (GetForegroundWindow() == IntPtr.Zero) Console.WriteLine("SKIP foreground checks: run --tray-popup-render on the interactive desktop");
            else using (var cover = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(240, 200, 400, 380) }) {
                cover.Show(); cover.Activate(); Pump(40);
                using (var popup = new TrayPopup(entries, new Point(600, 570), true, 1, true)) {
                    popup.Show(); Pump(300);
                    check(popup.Visible && GetForegroundWindow() == popup.Handle, "first popup takes foreground above an existing topmost flyout");
                    for (int visit = 0; visit < 3; visit++) {
                        popup.Navigate(entries.Single(i => i.Key == "flow:1")); Pump(40); popup.GoBack(); Pump(300);
                    }
                    var exit = popup.Rows.Single(r => r.Command.Key == "exit");
                    if (GetForegroundWindow() == popup.Handle) using (var capture = new Bitmap(popup.Width, popup.Height)) {
                        using (var graphics = Graphics.FromImage(capture)) graphics.CopyFromScreen(popup.Location, Point.Empty, capture.Size);
                        var point = popup.PointToClient(exit.PointToScreen(new Point(exit.Width / 2, exit.Height / 2)));
                        check(capture.GetPixel(point.X, point.Y).ToArgb() == new TrayPopup.PopupTheme(true).Card.ToArgb(), "composited exit row has its theme background after repeated short submenu returns");
                        capture.Save(Path.Combine(directory, "tray-popup-return-native.png"));
                    } else check(false, "native return capture requires the popup to retain foreground");
                    cover.Activate(); Pump(240);
                    check(!popup.Visible, "a later focus change still dismisses the popup");
                }
                Exception failure = null;
                int ownerThread = Thread.CurrentThread.ManagedThreadId, actionThread = 0, executed = 0;
                var livePreferences = new Preferences();
                Func<IList<TrayCommand>> liveSnapshot = () => TrayMenuModel.Create(State(), livePreferences, true, 0, p => { }, () => { }, r => {
                    actionThread = Thread.CurrentThread.ManagedThreadId; executed++; livePreferences.GameMode = r.Value;
                }, () => { });
                using (var host = new TrayPopupHost(action => cover.BeginInvoke(action), ex => failure = ex, liveSnapshot)) {
                    // Reproduce a second right click while the first STA is still starting.
                    host.Show(new Point(600, 570), liveSnapshot(), true);
                    host.Show(new Point(600, 570), liveSnapshot(), true);
                    Pump(600);
                    var foreground = GetForegroundWindow(); var title = new StringBuilder(128);
                    GetWindowText(foreground, title, title.Capacity);
                    check(host.Running && failure == null && IsWindowVisible(foreground) && title.ToString() == "声间 快捷菜单", "rapid right clicks preserve the latest open request on the separate STA");
                    host.Show(new Point(600, 570), liveSnapshot(), true); Pump(80);
                    check(GetForegroundWindow() == foreground && IsWindowVisible(foreground), "repeated right click reuses the visible popup instead of flashing a replacement");
                    var menu = Control.FromHandle(foreground) as TrayPopup;
                    check(menu != null, "hosted popup can be exercised without a real audio backend");
                    menu.BeginInvoke((Action)(() => menu.Rows.Single(r => r.Command.Key == "gameMode").PerformClick())); Pump(200);
                    check(executed == 1 && actionThread == ownerThread && livePreferences.GameMode && IsWindowVisible(foreground), "persistent command runs on the owner thread while the popup remains alive");
                    check((bool)menu.Invoke((Func<bool>)(() => !menu.Busy && menu.Rows.Single(r => r.Command.Key == "gameMode").Command.Checked)), "owner-thread result returns to the separate UI thread and updates the toggle");
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
