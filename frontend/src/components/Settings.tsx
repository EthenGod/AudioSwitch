import { Download, Upload, RefreshCw, ShieldCheck, ChevronRight, Moon, Sun } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { PreferenceKey, Snapshot } from '@/data/types'
import { SettingRow, ToggleRow } from './shared'
import { APP_VERSION } from '../version'
import { Select } from './ui/select'
import { Button } from './ui/button'

export function Settings({ snapshot, busy, readOnly = false, desktop = false, onPreference, onBrowserPort, onStartup, onBackup, onMaintenance }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; desktop?: boolean; onPreference: (key: PreferenceKey, value: boolean) => void
  onStartup: (value: boolean) => void;
  onBrowserPort: (port: number) => Promise<boolean>
  onBackup: (mode: 'import' | 'export') => void
  onMaintenance: (kind: 'update' | 'files') => void
}) {
  const prefs = snapshot.Preferences
  return <>
    <section className="settings-group" aria-label="应用偏好">
      <SettingRow title="双击托盘图标"><Select label="双击托盘图标" value={prefs.OpenPanelInBrowser ? 'browser' : 'panel'} options={[{ value: 'panel', label: '打开主面板' }, { value: 'browser', label: '在浏览器打开' }]} disabled={busy || readOnly || (desktop && !snapshot.CanSetTrayBehavior)} onValueChange={v => onPreference('OpenPanelInBrowser', v === 'browser')} /></SettingRow>
      <ToggleRow title="保持浏览器端口开放" description="随声间启动，关闭浏览器后仍保留本机服务。" value={prefs.KeepBrowserPortOpen} disabled={busy || readOnly || (desktop && !snapshot.CanKeepBrowserPortOpen)} onChange={v => onPreference('KeepBrowserPortOpen', v)} />
      <BrowserPort value={prefs.BrowserPort} disabled={busy || readOnly || (desktop && !snapshot.CanSetBrowserPort)} onSave={onBrowserPort} />
      <SettingRow title="外观"><div className="segmented"><button aria-label="浅色模式" aria-pressed={!prefs.DarkMode} disabled={busy} onClick={() => onPreference('DarkMode', false)}><Sun size={14} />浅色</button><button aria-label="深色模式" aria-pressed={prefs.DarkMode} disabled={busy} onClick={() => onPreference('DarkMode', true)}><Moon size={14} />深色</button></div></SettingRow>
      <ToggleRow title="游戏模式" description="暂停后台自动更新。" value={prefs.GameMode} disabled={busy || readOnly} onChange={v => onPreference('GameMode', v)} />
      <ToggleRow title="开机自启" description={desktop ? snapshot.StartupMessage || (snapshot.StartupAvailable && !readOnly ? '仅启动托盘。' : '当前连接无法修改自启设置。') : '仅启动托盘。'} value={snapshot.StartupEnabled} disabled={busy || readOnly || (desktop && !snapshot.StartupAvailable)} onChange={onStartup} />
      <ToggleRow title="自动更新" description="空闲时下载，下次启动安装。" value={prefs.AutoUpdateEnabled} disabled={busy || readOnly} onChange={v => onPreference('AutoUpdateEnabled', v)} />
    </section>
    <div className="section-heading"><h2>备份与维护</h2><span className="section-caption">{desktop && !snapshot.CanManageBackup ? '导入导出需要新版后台' : ''}</span></div>
    <section className="maintenance-grid">{[
      { title: '导出备份', Icon: Upload },
      { title: '导入设置', Icon: Download },
      { title: '检查更新', Icon: RefreshCw },
      { title: '文件检查', Icon: ShieldCheck },
    ].map(({ title, Icon }, index) => <button className="maintenance-item" key={title} disabled={busy || (desktop && (index < 2 ? readOnly || !snapshot.CanManageBackup : !snapshot.CanCheckMaintenance))} onClick={() => index < 2 ? onBackup(index === 0 ? 'export' : 'import') : onMaintenance(index === 2 ? 'update' : 'files')}><Icon size={20} /><div><strong>{title}</strong></div><ChevronRight size={16} /></button>)}</section>
    <div className="about-line"><img src="/mark.svg" width="30" height="30" alt="" /><div><strong>声间 <span>Audio Switch</span></strong><p>v{APP_VERSION} {desktop ? readOnly ? '兼容只读模式' : '桌面版' : '界面预览'}</p></div></div>
  </>
}

function BrowserPort({ value, disabled, onSave }: { value: number; disabled: boolean; onSave: (port: number) => Promise<boolean> }) {
  const saved = value === 0 ? '' : String(value)
  const [draft, setDraft] = useState(saved)
  useEffect(() => { setDraft(saved) }, [saved])
  const valid = draft === '' || (/^\d+$/.test(draft) && Number(draft) >= 1 && Number(draft) <= 65535)
  return <SettingRow title="浏览器端口" description="更改在下次开启服务时生效。">
    <form className="browser-port" onSubmit={async event => {
      event.preventDefault()
      if (!disabled && valid && draft !== saved && await onSave(draft === '' ? 0 : Number(draft))) setDraft(draft === '' ? '' : String(Number(draft)))
    }}>
      <div className="browser-port-controls">
        <input aria-label="端口号" inputMode="numeric" autoComplete="off" maxLength={5} placeholder="自动分配" value={draft} disabled={disabled} aria-invalid={!valid} aria-describedby={!valid ? 'browser-port-error' : undefined} onChange={event => setDraft(event.target.value)} />
        <Button variant="outline" type="button" disabled={disabled || draft === ''} onClick={() => setDraft('')}>自动</Button>
        <Button type="submit" disabled={disabled || !valid || draft === saved}>保存端口</Button>
      </div>
      {!valid && <p id="browser-port-error" role="alert">请输入 1–65535，或留空自动分配。</p>}
    </form>
  </SettingRow>
}
