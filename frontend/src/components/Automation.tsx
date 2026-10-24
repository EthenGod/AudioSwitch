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
      <ToggleRow title="按设备优先级选择" description="开启后立即选择排序靠前的在线设备；关闭后保留排序。" value={prefs.UseDevicePriority} disabled={busy || readOnly} onChange={v => onPreference('UseDevicePriority', v)} />
      <ToggleRow title="新设备接入时询问" description="设备接入时提供快捷选择；白名单规则优先。" value={prefs.AskOnConnect} disabled={busy || readOnly} onChange={v => onPreference('AskOnConnect', v)} />
      <ToggleRow title="同时切换通话设备" description="关闭后保留当前通话设备。" value={prefs.IncludeCommunications} disabled={busy || readOnly} onChange={v => onPreference('IncludeCommunications', v)} />
    </section>
    <div className="section-heading"><h2>设备优先级</h2><span className="section-caption">越靠前，优先级越高</span></div>
    <div className="list-toolbar"><div className="segmented" aria-label="优先级设备类型"><button aria-pressed={flow === 0} onClick={() => setFlow(0)}><Headphones size={14} />声音输出</button><button aria-pressed={flow === 1} onClick={() => setFlow(1)}><Mic size={14} />麦克风输入</button></div><span className="section-caption">{readOnly ? '只读' : ''}</span></div>
    <div className="priority-list" aria-label={flow === 0 ? '输出优先级列表' : '输入优先级列表'}>
      {order.map((id, index) => {
        const device = snapshot.Devices.find(d => d.Id === id)!
        return <div className="priority-row" key={id}><span className="order-number">{String(index + 1).padStart(2, '0')}</span><DeviceIdentity device={device} /><span className="priority-state">{device.Online ? '在线' : '离线'}</span><div className="row-actions"><Button variant="ghost" size="icon" aria-label={`上移 ${device.Name}`} disabled={busy || readOnly || index === 0} onClick={() => move(index, -1)}><ArrowUp /></Button><Button variant="ghost" size="icon" aria-label={`下移 ${device.Name}`} disabled={busy || readOnly || index === order.length - 1} onClick={() => move(index, 1)}><ArrowDown /></Button></div></div>
      })}
      {!order.length && <div className="empty-state"><p>暂无可排序的设备。</p></div>}
    </div>
    <p className="page-footnote">{readOnly ? '当前连接只支持查看。' : desktop ? '输出和麦克风分别排序。优先级开启时，调整排序会立即选择设备并应用预设。白名单优先。' : '白名单规则优先于设备排序。'}</p>
  </>
}
