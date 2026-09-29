// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioSwitch
{
    // Sample only while an automatic download is pending. No process inspection or audio polling.
    internal sealed class UpdateActivity : IDisposable
    {
        private IntPtr query, cpu, gpu, disk;
        private bool initialized;
        private int calm;
        internal string Diagnostic { get; private set; }
        internal static bool Busy(double cpuTotal, double cpuCore, double gpuEngine, double diskBusy, uint memory, bool fullscreen)
        {
            return fullscreen || new[] { cpuTotal, cpuCore, gpuEngine, diskBusy }.Any(v => Double.IsNaN(v) || Double.IsInfinity(v) || v < 0) ||
                cpuTotal >= 50 || cpuCore >= 85 || gpuEngine >= 60 || diskBusy >= 80 || memory >= 80;
        }
        internal bool ReadBusy(bool checkFullscreen = true)
        {
            try
            {
                // Window checks are cheap; skip all performance counters while a fullscreen app is present.
                if (checkFullscreen && Fullscreen()) { Dispose(); Diagnostic = "Fullscreen: load counters suspended"; return true; }
                var memory = new Memory { Length = (uint)Marshal.SizeOf(typeof(Memory)) };
                if (!GlobalMemoryStatusEx(ref memory) || memory.Load >= 80) { Dispose(); Diagnostic = "Memory pressure: load counters suspended"; return true; }
                if (!initialized)
                {
                    initialized = true;
                    if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return true;
                    PdhAddEnglishCounter(query, @"\Processor(*)\% Processor Time", IntPtr.Zero, out cpu);
                    PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out gpu);
                    PdhAddEnglishCounter(query, @"\PhysicalDisk(*)\% Idle Time", IntPtr.Zero, out disk);
                    PdhCollectQueryData(query); Diagnostic = "Warming up counters"; return true; // Rate counters need two samples.
                }
                if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) { calm = 0; return true; }
                var processors = Read(cpu); var engines = Read(gpu); var drives = Read(disk);
                Diagnostic = "CPU counters=" + processors.Count + ", GPU engines=" + engines.Count + ", disks=" + drives.Count;
                double total;
                if (!processors.TryGetValue("_Total", out total) || engines.Count == 0 || drives.Count == 0)
                { calm = 0; return true; }
                // Sum all processes using the same physical GPU engine; do not sum unrelated engines.
                double engine = engines.GroupBy(p => EngineKey(p.Key)).Max(g => g.Sum(p => p.Value));
                bool fullscreen = checkFullscreen && Fullscreen();
                Diagnostic += "; CPU=" + total.ToString("F1") + ", GPU=" + engine.ToString("F1") + ", disk=" + (100 - drives.Values.Min()).ToString("F1") + ", memory=" + memory.Load + ", fullscreen=" + fullscreen;
                bool busy = Busy(total, processors.Values.Max(), engine, 100 - drives.Values.Min(), memory.Load, fullscreen);
                calm = busy ? 0 : Math.Min(2, calm + 1);
                return calm < 2; // Two calm samples prevent repeated starts near a threshold.
            }
            catch (Exception ex) { Diagnostic = ex.Message; calm = 0; return true; }
        }
        internal static string EngineKey(string instance)
        { int index = instance.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase); return index >= 0 ? instance.Substring(index) : instance; }
        internal static bool Covers(int left, int top, int right, int bottom, int ml, int mt, int mr, int mb)
        { return right > left && bottom > top && left <= ml + 1 && top <= mt + 1 && right >= mr - 1 && bottom >= mb - 1; }
        internal static bool Fullscreen()
        {
            int state;
            if (SHQueryUserNotificationState(out state) != 0) return true;
            if (state == 2 || state == 3 || state == 4 || state == 7) return true;
            bool found = false;
            bool enumerated = EnumWindows((window, data) => {
                if (!IsWindowVisible(window) || IsIconic(window) || window == GetShellWindow()) return true;
                var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
                if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString().Contains("TrayWnd")) return true;
                int cloaked; if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                Rect bounds; var monitor = new Monitor { Size = (uint)Marshal.SizeOf(typeof(Monitor)) };
                if (GetWindowRect(window, out bounds) && GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor) &&
                    Covers(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Right, monitor.Bounds.Bottom))
                { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found || !enumerated;
        }
        private static Dictionary<string, double> Read(IntPtr counter)
        {
            var values = new Dictionary<string, double>();
            if (counter == IntPtr.Zero) return values;
            uint bytes = 0, count;
            if (PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out count, IntPtr.Zero) != 0x800007D2 || bytes == 0 || bytes > 4 * 1024 * 1024) return values;
            IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out count, buffer) != 0) return values;
                int size = Marshal.SizeOf(typeof(CounterItem));
                if ((long)count * size > bytes) return values;
                for (int i = 0; i < count; i++)
                {
                    var item = (CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(CounterItem));
                    if (item.Value.Status <= 1 && item.Name != IntPtr.Zero && !Double.IsNaN(item.Value.Number) && item.Value.Number >= 0)
                        values[Marshal.PtrToStringUni(item.Name)] = item.Value.Number;
                }
                return values;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; } initialized = false; calm = 0; }
        [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue { [FieldOffset(0)] internal uint Status; [FieldOffset(8)] internal double Number; }
        [StructLayout(LayoutKind.Sequential)] private struct CounterItem { internal IntPtr Name; internal CounterValue Value; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Monitor { internal uint Size; internal Rect Bounds, Work; internal uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct Memory { internal uint Length, Load; internal ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
        private delegate bool WindowCallback(IntPtr window, IntPtr data);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] private static extern uint PdhOpenQuery(string source, IntPtr data, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr data, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref Memory memory);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref Monitor info);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
    }
}
