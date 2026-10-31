// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AudioSwitch
{
    internal static class GameModeSchedulingTests
    {
        private sealed class Fake : IProcessScheduling
        {
            public ProcessPriorityClass Priority { get; set; }
            internal ProcessPowerState? Power = new ProcessPowerState { Version = 1, ControlMask = 4, StateMask = 4 };
            internal int Writes;
            internal bool Fail;
            public ProcessPowerState? ReadPower() { return Power; }
            public void WritePower(ProcessPowerState state)
            {
                Writes++; Power = state;
                if (Fail) throw new InvalidOperationException("write failed after change");
            }
        }
        internal static void Run(Action<bool, string> check)
        {
            var errors = new List<Exception>();
            var target = new Fake { Priority = ProcessPriorityClass.Normal };
            using (var policy = new GameModeScheduling(target, errors.Add))
            {
                policy.Apply(false);
                check(target.Writes == 0, "game mode off does not alter system scheduling");
                policy.Apply(true); policy.Apply(true);
                check(target.Priority == ProcessPriorityClass.BelowNormal && target.Writes == 1 && target.Power.Value.ControlMask == 5 && target.Power.Value.StateMask == 5,
                    "game mode lowers scheduling once and preserves other power flags");
                policy.Apply(false);
                check(target.Priority == ProcessPriorityClass.Normal && target.Power.Value.ControlMask == 4 && target.Power.Value.StateMask == 4,
                    "leaving game mode restores exact prior scheduling");
                policy.Apply(true); target.Fail = true; policy.Apply(false);
                check(target.Priority == ProcessPriorityClass.Normal && errors.Count == 1, "power restore failure does not prevent priority restore");
                target.Fail = false;
            }
            check(target.Power.Value.ControlMask == 4 && target.Writes == 5, "disposal retries failed restore without losing original power state");
            target = new Fake { Priority = ProcessPriorityClass.Idle, Power = null };
            using (var policy = new GameModeScheduling(target, errors.Add)) {
                policy.Apply(true);
                check(target.Priority == ProcessPriorityClass.Idle && target.Writes == 0, "unsupported EcoQoS and existing lower priority are preserved");
            }
            target = new Fake { Priority = ProcessPriorityClass.Normal, Fail = true };
            using (var policy = new GameModeScheduling(target, errors.Add)) {
                policy.Apply(true); target.Fail = false; policy.Apply(false);
                check(target.Power.Value.ControlMask == 4 && target.Priority == ProcessPriorityClass.Normal, "partially failed optimization still restores original state");
            }
            target = new Fake { Priority = ProcessPriorityClass.BelowNormal, Power = new ProcessPowerState { Version = 1, ControlMask = 1, StateMask = 1 } };
            using (var policy = new GameModeScheduling(target, errors.Add)) { policy.Apply(true); }
            check(target.Writes == 0 && target.Priority == ProcessPriorityClass.BelowNormal, "preexisting efficiency mode is not disabled on exit");

            // Exercise only this test process. No tray/backend launch, config, or audio writes.
            var native = new CurrentProcessScheduling();
            var originalPriority = native.Priority; var originalPower = native.ReadPower();
            errors.Clear();
            try {
                using (var policy = new GameModeScheduling(native, errors.Add)) {
                    policy.Apply(true);
                    check(native.Priority == ProcessPriorityClass.BelowNormal || native.Priority == ProcessPriorityClass.Idle, "native game mode priority readback");
                    var active = native.ReadPower();
                    check(!originalPower.HasValue || (active.HasValue && (active.Value.ControlMask & active.Value.StateMask & 1) == 1), "native EcoQoS readback when supported");
                    policy.Apply(false);
                    var restored = native.ReadPower();
                    check(native.Priority == originalPriority && (!originalPower.HasValue || (restored.HasValue && restored.Value.ControlMask == originalPower.Value.ControlMask && restored.Value.StateMask == originalPower.Value.StateMask)), "native scheduling restored and independently read back");
                    check(errors.Count == 0, "native scheduling completed without ignored failures");
                }
            } finally {
                try { if (originalPower.HasValue) native.WritePower(originalPower.Value); }
                finally { native.Priority = originalPriority; }
            }
        }
    }
}
