import { useState } from 'react'
import { ArrowDown, ArrowUp, Headphones, Mic } from 'lucide-react'
import type { Flow, PreferenceKey, Snapshot } from '@/data/types'
import { Button } from './ui/button'
import { DeviceIdentity, ToggleRow } from './shared'

export function Automation({ snapshot, busy, readOnly = false, desktop = false, onPreference, onReorder }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; desktop?: boolean; onPreference: (key: PreferenceKey, value: boolean) => void
  onReorder: (flow: Flow, ids: string[]) => void
}) {
  const [flow, setFlow] = useState<Flow>(0)
  const prefs = snapshot.Preferences
  const order = prefs.DeviceOrder[flow]
  const move = (index: number, delta: number) => {
    const next = [...order]; [next[index], next[index + delta]] = [next[index + delta], next[index]]
    onReorder(flow, next)
  }
  return <>
    <section className="settings-group" aria-label="自动切换选项">
      <ToggleRow title="按设备优先级选择" description="开启后立即切换到优先设备。" value={prefs.UseDevicePriority} disabled={busy || readOnly} onChange={v => onPreference('UseDevicePriority', v)} />
      <ToggleRow title="新设备接入时询问" description="已设接入规则的设备不询问。" value={prefs.AskOnConnect} disabled={busy || readOnly} onChange={v => onPreference('AskOnConnect', v)} />
      <ToggleRow title="同时切换通话设备" value={prefs.IncludeCommunications} disabled={busy || readOnly} onChange={v => onPreference('IncludeCommunications', v)} />
    </section>
    <div className="section-heading"><h2>设备优先级</h2></div>
    <div className="list-toolbar"><div className="segmented" aria-label="优先级设备类型"><button aria-pressed={flow === 0} onClick={() => setFlow(0)}><Headphones size={14} />声音输出</button><button aria-pressed={flow === 1} onClick={() => setFlow(1)}><Mic size={14} />麦克风输入</button></div><span className="section-caption">{readOnly ? '只读' : ''}</span></div>
    {desktop && !readOnly && prefs.UseDevicePriority && <p className="priority-notice">调整排序会立即切换设备并应用预设，白名单优先。</p>}
    <div className="priority-list" aria-label={flow === 0 ? '输出优先级列表' : '输入优先级列表'}>
      {order.map((id, index) => {
        const device = snapshot.Devices.find(d => d.Id === id)!
        return <div className="priority-row" key={id}><span className="order-number">{String(index + 1).padStart(2, '0')}</span><DeviceIdentity device={device} /><span className="priority-state">{device.Online ? '在线' : '离线'}</span><div className="row-actions"><Button variant="ghost" size="icon" aria-label={`上移 ${device.Name}`} disabled={busy || readOnly || index === 0} onClick={() => move(index, -1)}><ArrowUp /></Button><Button variant="ghost" size="icon" aria-label={`下移 ${device.Name}`} disabled={busy || readOnly || index === order.length - 1} onClick={() => move(index, 1)}><ArrowDown /></Button></div></div>
      })}
      {!order.length && <div className="empty-state"><p>暂无可排序的设备。</p></div>}
    </div>
  </>
}
