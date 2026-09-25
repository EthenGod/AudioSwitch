// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Windows.Forms;

namespace AudioSwitch
{
    // A burst gets one bounded wait from its first event, never a moving deadline.
    internal sealed class DeviceEventBatch : IDisposable
    {
        private readonly Timer timer = new Timer { Interval = 150 };
        internal DeviceEventBatch(Action refresh)
        {
            timer.Tick += delegate { timer.Stop(); refresh(); };
        }
        internal void Signal() { if (!timer.Enabled) timer.Start(); }
        internal void Stop() { timer.Stop(); }
        public void Dispose() { timer.Dispose(); }
    }
}
