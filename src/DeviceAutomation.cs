using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    internal sealed class DeviceDecision
    {
        internal int Flow;
        internal Endpoint Target;
        internal bool Quiet;
    }

    // Event rules are evaluated before priority selection and before opening any UI.
    internal static class DeviceAutomation
    {
        internal static DeviceRule Rule(Preferences preferences, string id)
        {
            DeviceRule rule;
            return id != null && preferences.DeviceRules != null && preferences.DeviceRules.TryGetValue(id, out rule)
                && Enum.IsDefined(typeof(DeviceRule), rule) ? rule : DeviceRule.Normal;
        }

        internal static List<DeviceDecision> Decide(AudioState previous, AudioState next, Preferences preferences)
        {
            var decisions = new List<DeviceDecision>();
            var priority = DevicePriority.Targets(previous, next, preferences);
            foreach (int flow in new[] { 0, 1 })
            {
                var oldIds = new HashSet<string>(previous.Devices.Where(d => d.Flow == flow).Select(d => d.Id));
                var arrivals = next.Devices.Where(d => d.Flow == flow && !oldIds.Contains(d.Id)).ToList();
                var automatic = DevicePriority.Ordered(arrivals.Where(d => Rule(preferences, d.Id) == DeviceRule.SwitchOnConnect), preferences, next, flow).FirstOrDefault();
                if (automatic != null)
                {
                    decisions.Add(new DeviceDecision { Flow = flow, Target = automatic, Quiet = true });
                    continue;
                }
                string current = next.Default(flow, 1);
                bool selected = current != previous.Default(flow, 1) || arrivals.Any(d => d.Id == current);
                if (selected && next.Devices.Any(d => d.Id == current && d.Flow == flow) && Rule(preferences, current) == DeviceRule.AcceptSystem)
                {
                    // Accept Windows' roles and current settings exactly as they are.
                    decisions.Add(new DeviceDecision { Flow = flow, Quiet = true });
                    continue;
                }
                var target = priority.FirstOrDefault(d => d.Flow == flow);
                if (target != null) decisions.Add(new DeviceDecision { Flow = flow, Target = target, Quiet = Rule(preferences, target.Id) != DeviceRule.Normal });
            }
            return decisions;
        }

        internal static List<string> Process(ArrivalTracker tracker, AudioState next, Preferences preferences, Action<Endpoint> apply, out bool notify, out bool handled)
        {
            var decisions = Decide(tracker.Current, next, preferences);
            handled = decisions.Count > 0;
            bool arrival = tracker.Update(next, preferences.AskOnConnect, true, preferences.IncludeCommunications);
            var errors = new List<string>();
            foreach (var decision in decisions)
            {
                try
                {
                    if (decision.Target != null) apply(decision.Target);
                    // Clear only after success; failed switches keep their manual recovery choices.
                    if (decision.Quiet) tracker.Pending.RemoveAll(p => p.Flow == decision.Flow);
                }
                catch (Exception ex)
                {
                    errors.Add((decision.Target == null ? "设备" : decision.Target.Name) + "：" + ex.Message);
                }
            }
            notify = arrival && tracker.Pending.Count > 0;
            return errors;
        }

        internal static List<Endpoint> StartupTargets(AudioState current, Preferences preferences)
        {
            return DevicePriority.Targets(current, current, preferences, null, true)
                .Where(d => Rule(preferences, current.Default(d.Flow, 1)) != DeviceRule.AcceptSystem).ToList();
        }
    }
}
