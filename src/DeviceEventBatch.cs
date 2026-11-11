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
        private int posted;
        private bool stopped;
        internal DeviceEventBatch(Action refresh)
        {
            timer.Tick += delegate { timer.Stop(); refresh(); };
        }
        // Core Audio may notify from several threads while the owner is busy.
        // Queue one wake-up instead of one delegate for every driver notification.
        internal void Post(Action<Action> dispatch, Action onSignal)
        {
            if (System.Threading.Interlocked.Exchange(ref posted, 1) != 0) return;
            try
            {
                dispatch(() => {
                    System.Threading.Interlocked.Exchange(ref posted, 0);
                    if (stopped) return;
                    onSignal(); Signal();
                });
            }
            catch
            {
                System.Threading.Interlocked.Exchange(ref posted, 0);
                throw;
            }
        }
        internal void Signal() { if (!stopped && !timer.Enabled) { timer.Start(); } }
        internal void Stop() { stopped = true; timer.Stop(); }
        public void Dispose() { Stop(); timer.Dispose(); }
    }
}
