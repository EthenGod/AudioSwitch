import { useState } from 'react'
import { Headphones, Mic } from 'lucide-react'
import type { Flow, PreferenceKey, Snapshot } from '@/data/types'
import { ToggleRow } from './shared'
import { PriorityList } from './PriorityList'

export function Automation({ snapshot, busy, readOnly = false, desktop = false, onPreference, onReorder }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; desktop?: boolean; onPreference: (key: PreferenceKey, value: boolean) => void
  onReorder: (flow: Flow, ids: string[]) => Promise<boolean>
}) {
  const [flow, setFlow] = useState<Flow>(0)
  const prefs = snapshot.Preferences
  const order = prefs.DeviceOrder[flow]
  return <>
    <section className="settings-group" aria-label="自动切换选项">
      <ToggleRow title="按设备优先级选择" description="开启后立即切换到优先设备。" value={prefs.UseDevicePriority} pending={busy} disabled={readOnly} onChange={v => onPreference('UseDevicePriority', v)} />
      <ToggleRow title="新设备接入时询问" description="已设接入规则的设备不询问。" value={prefs.AskOnConnect} pending={busy} disabled={readOnly} onChange={v => onPreference('AskOnConnect', v)} />
      <ToggleRow title="同时切换通话设备" value={prefs.IncludeCommunications} pending={busy} disabled={readOnly} onChange={v => onPreference('IncludeCommunications', v)} />
    </section>
    <div className="section-heading"><h2>设备优先级</h2></div>
    <div className="list-toolbar"><div className="segmented" aria-label="优先级设备类型"><button aria-pressed={flow === 0} onClick={() => setFlow(0)}><Headphones size={14} />声音输出</button><button aria-pressed={flow === 1} onClick={() => setFlow(1)}><Mic size={14} />麦克风输入</button></div><span className="section-caption">{readOnly ? '只读' : ''}</span></div>
    {desktop && !readOnly && prefs.UseDevicePriority && <p className="priority-notice">调整排序会立即切换设备并应用预设，白名单优先。</p>}
    <PriorityList key={flow} order={order} devices={snapshot.Devices} busy={busy} readOnly={readOnly} label={flow === 0 ? '输出优先级列表' : '输入优先级列表'} onReorder={ids => onReorder(flow, ids)} />
  </>
}
