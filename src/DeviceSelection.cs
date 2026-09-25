using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    internal static class DeviceSelection
    {
        // Keep policy separate from COM so failure/rollback behavior can be exercised safely.
        internal static void Apply(int flow, Dictionary<int, string> targets, Func<AudioState> read, Action<string, int> set)
        {
            var before = read();
            if (targets.Any(p => !before.Devices.Any(d => d.Flow == flow && d.Id == p.Value)))
                throw new InvalidOperationException("设备已断开，请选择其他设备。");
            var changed = new List<int>();
            try
            {
                foreach (var pair in targets)
                {
                    if (before.Default(flow, pair.Key) == pair.Value) continue;
                    // A driver can perform the write and then report a failure.
                    changed.Add(pair.Key); set(pair.Value, pair.Key);
                }
                var actual = read();
                if (targets.Any(p => actual.Default(flow, p.Key) != p.Value)) throw new InvalidOperationException("Windows 尚未采用所选设备。");
            }
            catch (Exception ex)
            {
                bool rollbackFailed = false;
                foreach (int role in changed.AsEnumerable().Reverse())
                {
                    try { var old = before.Default(flow, role); if (old != null) set(old, role); else rollbackFailed = true; }
                    catch { rollbackFailed = true; }
                }
                try
                {
                    var restored = read();
                    if (changed.Any(role => restored.Default(flow, role) != before.Default(flow, role))) rollbackFailed = true;
                }
                catch { rollbackFailed = true; }
                throw new InvalidOperationException("切换失败：" + ex.Message + (rollbackFailed ? " 部分设置无法恢复，请检查系统声音设置。" : " 已恢复原设置。"));
            }
        }
    }
}
