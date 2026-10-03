import { useState } from 'react'
import { ArrowRight, Check, ChevronRight, Headphones, Mic, Search, SlidersHorizontal, Unplug } from 'lucide-react'
import type { Device, Flow, Snapshot } from '@/data/types'
import { Button } from './ui/button'
import { DeviceIdentity, RoleBadges } from './shared'

export function Devices({ snapshot, busy, readOnly = false, onSwitch, onSettings, onAutomation }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; onSwitch: (device: Device) => void
  onSettings: (device: Device) => void; onAutomation: () => void
}) {
  const [filter, setFilter] = useState<Flow | -1>(-1)
  const [query, setQuery] = useState('')
  const visible = snapshot.Devices.filter(d => (filter === -1 || d.Flow === filter) && `${d.Name} ${d.Connection}`.toLowerCase().includes(query.toLowerCase().trim()))
  return <>
    <div className="summary-grid">{([0, 1] as Flow[]).map(flow => {
      const current = snapshot.Devices.find(d => d.Id === snapshot.Defaults[`${flow}:1`])
      const Icon = flow === 0 ? Headphones : Mic
      return <section className="summary-card" key={flow} aria-label={flow === 0 ? '当前声音输出' : '当前麦克风'}>
        <div className="summary-top"><span><Icon size={14} />{flow === 0 ? '当前声音输出' : '当前麦克风'}</span><span className="tiny-label">多媒体默认</span></div>
        <div className="summary-device"><div className="min-w-0"><h2 title={current?.Name}>{current?.Name ?? '暂无可用设备'}</h2><p>{current?.Connection ?? '连接设备后会显示在这里'}</p></div><Button variant="ghost" size="icon" disabled={!current || busy} aria-label={flow === 0 ? '设置当前输出' : '设置当前麦克风'} onClick={() => current && onSettings(current)}><ChevronRight /></Button></div>
      </section>
    })}</div>
    <section className="device-section" aria-labelledby="device-list-title">
      <div className="section-heading"><h2 id="device-list-title">设备列表 <span className="count">{snapshot.Devices.length}</span></h2><span className="section-caption">{snapshot.Devices.filter(d => d.Online).length} 台在线 · 按设备独立保存预设</span></div>
      <div className="list-toolbar">
        <div className="segmented" aria-label="设备类型筛选">{([-1, 0, 1] as const).map((value, index) => <button key={value} aria-pressed={filter === value} onClick={() => setFilter(value)}>{['全部设备', '声音输出', '麦克风输入'][index]}</button>)}</div>
        <label className="search-field"><Search size={14} /><input aria-label="搜索设备" placeholder="搜索设备…" value={query} onChange={e => setQuery(e.target.value)} />{query && <button aria-label="清空搜索" onClick={() => setQuery('')}>×</button>}</label>
      </div>
      <div className="device-table" role="table" aria-label="音频设备">
        <div className="device-table-head" role="row"><span role="columnheader">设备名称</span><span role="columnheader">默认角色 / 状态</span><span role="columnheader" className="text-right">操作</span></div>
        {visible.map(device => {
          const current = snapshot.Defaults[`${device.Flow}:1`] === device.Id
          return <div role="row" key={device.Id} className={`device-row ${current ? 'is-current' : ''}`} data-device-id={device.Id}>
            <div role="cell"><DeviceIdentity device={device} /></div>
            <div role="cell"><RoleBadges device={device} snapshot={snapshot} /></div>
            <div role="cell" className="row-actions">
              <Button variant="ghost" size="sm" disabled={readOnly || !device.Online || busy || current} aria-label={`${current ? '正在使用' : '切换到'} ${device.Name} · ${device.Connection}`} onClick={() => onSwitch(device)}>{current ? <Check /> : <ArrowRight />}{current ? '使用中' : readOnly ? '只读' : '切换'}</Button>
              <Button variant="ghost" size="icon" disabled={busy} aria-label={`设置 ${device.Name} · ${device.Connection}`} title="设备设置" onClick={() => onSettings(device)}><SlidersHorizontal /></Button>
            </div>
          </div>
        })}
        {!visible.length && <div className="empty-state"><Unplug size={28} /><h3>{snapshot.Devices.length ? '没有找到匹配的设备' : '还没有声音设备'}</h3><p>{snapshot.Devices.length ? '试试其他名称，或切换设备类型。' : '连接耳机、扬声器或麦克风后，设备会显示在这里。'}</p>{(query || filter !== -1) && <Button variant="outline" onClick={() => { setQuery(''); setFilter(-1) }}>清除筛选</Button>}</div>}
      </div>
      <div className="list-footnote"><span>离线设备的排序与预设会保留。</span><button onClick={onAutomation}>管理自动切换 <ChevronRight size={13} /></button></div>
    </section>
  </>
}
