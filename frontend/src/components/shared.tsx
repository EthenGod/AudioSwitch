import type { ReactNode } from 'react'
import { Headphones, Speaker, Mic, Monitor, Check, Circle } from 'lucide-react'
import type { Device, Snapshot, Role } from '@/data/types'
import { Switch } from './ui/switch'

export function DeviceIcon({ device, size = 19 }: { device: Device; size?: number }) {
  const Icon = { headphones: Headphones, speaker: Speaker, microphone: Mic, monitor: Monitor }[device.Kind]
  return <Icon size={size} aria-hidden="true" data-device-kind={device.Kind} />
}
export function DeviceIdentity({ device }: { device: Device }) {
  return <div className="device-identity">
    <span className={`device-icon ${!device.Online ? 'offline' : ''}`}><DeviceIcon device={device} /></span>
    <div className="min-w-0"><div className="device-name" title={`${device.Name}\n设备标识：${device.Id}`}>{device.Name}</div><div className="device-description">{device.Connection}</div></div>
  </div>
}
export function RoleBadges({ device, snapshot }: { device: Device; snapshot: Snapshot }) {
  const active = ([0, 1, 2] as Role[]).filter(role => snapshot.Defaults[`${device.Flow}:${role}`] === device.Id)
  if (!device.Online) return <span className="subtle-label"><Circle size={7} /> 离线</span>
  return <div className="badges">{active.length ? active.map(role => <span className={`role-badge ${role === 1 ? 'primary-badge' : ''}`} key={role}>{role === 1 && <Check size={11} />}{['控制台', '多媒体', '通话'][role]}</span>) : <span className="subtle-label"><span className="online-dot" /> 可用</span>}</div>
}
export function SettingRow({ title, description, children, muted = false }: { title: string; description?: ReactNode; children: ReactNode; muted?: boolean }) {
  return <div className="setting-row" data-muted={muted || undefined}><div><div className="setting-title">{title}</div>{description && <p>{description}</p>}</div><div className="setting-control">{children}</div></div>
}
export function ToggleRow({ title, description, value, disabled, pending, muted, onChange }: { title: string; description?: string; value: boolean; disabled?: boolean; pending?: boolean; muted?: boolean; onChange: (value: boolean) => void }) {
  return <SettingRow title={title} description={description} muted={muted}><Switch aria-label={title} checked={value} disabled={disabled} pending={pending} onCheckedChange={onChange} /></SettingRow>
}
