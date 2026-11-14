import { t } from '../i18n'
import { useState } from 'react'
import { ArrowRight, Check, ChevronRight, Headphones, Mic, Search, SlidersHorizontal, Unplug } from 'lucide-react'
import type { Device, Flow, Snapshot } from '@/data/types'
import { Button } from './ui/button'
import { DeviceIcon, DeviceIdentity, RoleBadges } from './shared'
import { DeviceListMotion } from './DeviceListMotion'
import { deviceDescription } from '@/data/deviceDescription'

export function Devices({ snapshot, busy, readOnly = false, onSwitch, onSettings, onAutomation }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; onSwitch: (device: Device) => void
  onSettings: (device: Device) => void; onAutomation: () => void
}) {
  const [filter, setFilter] = useState<Flow | -1>(-1)
  const [query, setQuery] = useState('')
  const visible = snapshot.Devices.filter(d => (filter === -1 || d.Flow === filter) && `${d.Name} ${d.SystemName ?? ''} ${deviceDescription(d)}`.toLowerCase().includes(query.toLowerCase().trim()))
  return <>
    <div className="summary-grid">{([0, 1] as Flow[]).map(flow => {
      const current = snapshot.Devices.find(d => d.Id === snapshot.Defaults[`${flow}:1`])
      const Icon = flow === 0 ? Headphones : Mic
      return <section className="summary-card" key={flow} aria-label={flow === 0 ? t("当前声音输出") : t("当前麦克风")}>
        <div className="summary-top"><span>{current ? <DeviceIcon device={current} size={14} /> : <Icon size={14} />}{flow === 0 ? t("当前声音输出") : t("当前麦克风")}</span></div>
        <div className="summary-device"><div className="min-w-0"><h2 title={current?.Name}>{current?.Name ?? t("暂无可用设备")}</h2><p>{current && deviceDescription(current)}</p></div><Button variant="ghost" size="icon" pending={busy} disabled={!current} aria-label={flow === 0 ? t("设置当前输出") : t("设置当前麦克风")} onClick={() => current && onSettings(current)}><ChevronRight /></Button></div>
      </section>
    })}</div>
    <section className="device-section" aria-labelledby="device-list-title">
      <div className="section-heading"><h2 id="device-list-title">{t("设备列表")}<span className="count">{snapshot.Devices.length}</span></h2><span className="section-caption">{t("{0} 台在线", snapshot.Devices.filter(d => d.Online).length)}</span></div>
      <div className="list-toolbar">
        <div className="segmented" aria-label={t("设备类型筛选")}>{([-1, 0, 1] as const).map((value, index) => <button key={value} aria-pressed={filter === value} onClick={() => setFilter(value)}>{[t("全部设备"), t("声音输出"), t("麦克风输入")][index]}</button>)}</div>
        <label className="search-field"><Search size={14} /><input aria-label={t("搜索设备")} placeholder={t("搜索设备…")} value={query} onChange={e => setQuery(e.target.value)} />{query && <button aria-label={t("清空搜索")} onClick={() => setQuery('')}>×</button>}</label>
      </div>
      <div className="device-table" role="table" aria-label={t("音频设备")} tabIndex={-1}>
        <div className="device-table-head" role="row"><span role="columnheader">{t("设备名称")}</span><span role="columnheader">{t("默认角色 / 状态")}</span><span role="columnheader" className="text-right">{t("操作")}</span></div>
        <DeviceListMotion devices={visible}>{device => {
          const current = (snapshot.CanWrite ? (snapshot.Preferences.IncludeCommunications ? [0, 1, 2] as const : [0, 1] as const) : [1] as const).every(role => snapshot.Defaults[`${device.Flow}:${role}`] === device.Id)
          return <div role="row" key={device.Id} className={`device-row ${current ? 'is-current' : ''}`} data-device-id={device.Id} data-online={device.Online}>
            <div role="cell"><DeviceIdentity device={device} /></div>
            <div role="cell"><RoleBadges device={device} snapshot={snapshot} /></div>
            <div role="cell" className="row-actions">
              <Button variant="ghost" size="sm" pending={busy} disabled={readOnly || !device.Online || current} aria-label={current ? t('正在使用 {0}，{1}', device.Name, deviceDescription(device)) : t('切换到 {0}，{1}', device.Name, deviceDescription(device))} onClick={() => onSwitch(device)}>{current ? <Check /> : <ArrowRight />}{current ? t("使用中") : readOnly ? t("只读") : t("切换")}</Button>
              <Button variant="ghost" size="icon" pending={busy} aria-label={t("设置 {0}，{1}", device.Name, deviceDescription(device))} title={t("设备设置")} onClick={() => onSettings(device)}><SlidersHorizontal /></Button>
            </div>
          </div>
        }}</DeviceListMotion>
        {!visible.length && <div className="empty-state"><Unplug size={28} /><h3>{snapshot.Devices.length ? t("没有找到匹配的设备") : t("还没有声音设备")}</h3><p>{snapshot.Devices.length ? t("更换关键词或清除筛选") : t("请连接耳机、扬声器或麦克风")}</p>{(query || filter !== -1) && <Button variant="outline" onClick={() => { setQuery(''); setFilter(-1) }}>{t("清除筛选")}</Button>}</div>}
      </div>
      <div className="list-footnote"><button onClick={onAutomation}>{t("管理自动切换")}<ChevronRight size={13} /></button></div>
    </section>
  </>
}
