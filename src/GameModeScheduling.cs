// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioSwitch
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessPowerState
    {
        internal uint Version, ControlMask, StateMask;
    }
    internal interface IProcessScheduling
    {
        ProcessPriorityClass Priority { get; set; }
        ProcessPowerState? ReadPower();
        void WritePower(ProcessPowerState value);
    }
    // Only the tray backend owns this policy. Never target unrelated processes or WebView renderers.
    internal sealed class GameModeScheduling : IDisposable
    {
        private readonly IProcessScheduling process;
        private readonly Action<Exception> report;
        private ProcessPriorityClass? priority;
        private ProcessPowerState? power;
        internal GameModeScheduling() : this(new CurrentProcessScheduling(), Program.Log) { }
        internal GameModeScheduling(IProcessScheduling process, Action<Exception> report)
        { this.process = process; this.report = report; }
        private void Attempt(Action action)
        {
            try { action(); }
            catch (Exception ex) { report(ex); } // An unavailable optimization must not interrupt audio handling.
        }
        internal void Apply(bool enabled)
        {
            if (enabled)
            {
                if (!priority.HasValue) Attempt(() => {
                    var before = process.Priority;
                    if (before == ProcessPriorityClass.Idle || before == ProcessPriorityClass.BelowNormal) return;
                    priority = before; // Keep the original even if a write partially succeeds then fails.
                    process.Priority = ProcessPriorityClass.BelowNormal;
                });
                if (!power.HasValue) Attempt(() => {
                    var before = process.ReadPower();
                    if (!before.HasValue || (before.Value.ControlMask & before.Value.StateMask & 1) != 0) return;
                    power = before;
                    var next = before.Value;
                    next.ControlMask |= 1; next.StateMask |= 1; // EXECUTION_SPEED: EcoQoS on supported Windows.
                    process.WritePower(next);
                });
            }
            else
            {
                if (power.HasValue) Attempt(() => { process.WritePower(power.Value); power = null; });
                if (priority.HasValue) Attempt(() => { process.Priority = priority.Value; priority = null; });
            }
        }
        public void Dispose() { Apply(false); }
    }
    internal sealed class CurrentProcessScheduling : IProcessScheduling
    {
        public ProcessPriorityClass Priority
        {
            get { using (var process = Process.GetCurrentProcess()) return process.PriorityClass; }
            set { using (var process = Process.GetCurrentProcess()) process.PriorityClass = value; }
        }
        public ProcessPowerState? ReadPower()
        {
            var state = new ProcessPowerState { Version = 1 };
            try
            {
                if (GetProcessInformation(GetCurrentProcess(), 4, ref state, 12)) return state;
                int error = Marshal.GetLastWin32Error();
                if (error == 87 || error == 50 || error == 120) return null;
                throw new Win32Exception(error);
            }
            catch (EntryPointNotFoundException) { return null; }
        }
        public void WritePower(ProcessPowerState value)
        {
            if (!SetProcessInformation(GetCurrentProcess(), 4, ref value, 12)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessInformation(IntPtr process, int kind, ref ProcessPowerState state, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(IntPtr process, int kind, ref ProcessPowerState state, uint size);
    }
}
