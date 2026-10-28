import { Download, Upload, RefreshCw, ShieldCheck, ChevronRight, Moon, Sun } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { PreferenceKey, Snapshot } from '@/data/types'
import { SettingRow, ToggleRow } from './shared'
import { APP_VERSION } from '../version'
import { Select } from './ui/select'

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
      <SettingRow title="双击托盘图标"><Select label="双击托盘图标" value={prefs.OpenPanelInBrowser ? 'browser' : 'panel'} options={[{ value: 'panel', label: '打开主面板' }, { value: 'browser', label: '在浏览器打开' }]} pending={busy} disabled={readOnly || (desktop && !snapshot.CanSetTrayBehavior)} onValueChange={v => onPreference('OpenPanelInBrowser', v === 'browser')} /></SettingRow>
      <ToggleRow title="保持浏览器端口开放" description="随声间启动，关闭浏览器后仍保留本机服务。" value={prefs.KeepBrowserPortOpen} pending={busy} disabled={readOnly || (desktop && !snapshot.CanKeepBrowserPortOpen)} onChange={v => onPreference('KeepBrowserPortOpen', v)} />
      <BrowserPort value={prefs.BrowserPort} pending={busy} disabled={readOnly || (desktop && !snapshot.CanSetBrowserPort)} onSave={onBrowserPort} />
      <SettingRow title="外观"><div className="segmented"><button aria-label="浅色模式" aria-pressed={!prefs.DarkMode} data-pending={busy || undefined} disabled={busy} onClick={() => onPreference('DarkMode', false)}><Sun size={14} />浅色</button><button aria-label="深色模式" aria-pressed={prefs.DarkMode} data-pending={busy || undefined} disabled={busy} onClick={() => onPreference('DarkMode', true)}><Moon size={14} />深色</button></div></SettingRow>
      <ToggleRow title="游戏模式" description="暂停后台自动更新。" value={prefs.GameMode} pending={busy} disabled={readOnly} onChange={v => onPreference('GameMode', v)} />
      <ToggleRow title="开机自启" description={desktop ? snapshot.StartupMessage || (snapshot.StartupAvailable && !readOnly ? '仅启动托盘。' : '当前连接无法修改自启设置。') : '仅启动托盘。'} value={snapshot.StartupEnabled} pending={busy} disabled={readOnly || (desktop && !snapshot.StartupAvailable)} onChange={onStartup} />
      <ToggleRow title="自动更新" description="空闲时下载，下次启动安装。" value={prefs.AutoUpdateEnabled} pending={busy} disabled={readOnly} onChange={v => onPreference('AutoUpdateEnabled', v)} />
    </section>
    <div className="section-heading"><h2>备份与维护</h2><span className="section-caption">{desktop && !snapshot.CanManageBackup ? '导入导出需要新版后台' : ''}</span></div>
    <section className="maintenance-grid">{[
      { title: '导出备份', Icon: Upload },
      { title: '导入设置', Icon: Download },
      { title: '检查更新', Icon: RefreshCw },
      { title: '文件检查', Icon: ShieldCheck },
    ].map(({ title, Icon }, index) => <button className="maintenance-item" key={title} data-pending={busy && !(desktop && (index < 2 ? readOnly || !snapshot.CanManageBackup : !snapshot.CanCheckMaintenance)) || undefined} disabled={busy || (desktop && (index < 2 ? readOnly || !snapshot.CanManageBackup : !snapshot.CanCheckMaintenance))} onClick={() => index < 2 ? onBackup(index === 0 ? 'export' : 'import') : onMaintenance(index === 2 ? 'update' : 'files')}><Icon size={20} /><div><strong>{title}</strong></div><ChevronRight size={16} /></button>)}</section>
    <div className="about-line"><img src="/mark.svg" width="30" height="30" alt="" /><div><strong>声间 <span>Audio Switch</span></strong><p>v{APP_VERSION} {desktop ? readOnly ? '兼容只读模式' : '桌面版' : '界面预览'}</p></div></div>
  </>
}

export function BrowserPort({ value, disabled, pending = false, onSave }: { value: number; disabled: boolean; pending?: boolean; onSave: (port: number) => Promise<boolean> }) {
  const saved = value === 0 ? '' : String(value)
  const [draft, setDraft] = useState(saved)
  const [failed, setFailed] = useState(false)
  const attempted = useRef<string | null>(null)
  const saving = useRef(false)
  const save = useRef(onSave); save.current = onSave
  useEffect(() => { setDraft(saved) }, [saved])
  const valid = draft === '' || (/^\d+$/.test(draft) && Number(draft) >= 1 && Number(draft) <= 65535)
  async function commit(retry = false) {
    if (disabled || pending || saving.current || !valid || draft === saved || (!retry && attempted.current === draft)) return
    attempted.current = draft; saving.current = true; setFailed(false)
    try {
      if (await save.current(draft === '' ? 0 : Number(draft))) setDraft(draft === '' ? '' : String(Number(draft)))
      else setFailed(true)
    } catch { setFailed(true) }
    finally { saving.current = false }
  }
  const commitRef = useRef(commit); commitRef.current = commit
  useEffect(() => {
    if (disabled || pending || !valid || draft === saved) return
    const timer = setTimeout(() => { void commitRef.current() }, 600)
    return () => clearTimeout(timer)
  }, [draft, saved, valid, disabled, pending])
  return <SettingRow title="浏览器端口" description={!valid || failed ? <span className="setting-error" id="browser-port-error" role="alert">{!valid ? '请输入 1–65535，或留空。' : '未保存，按 Enter 重试。'}</span> : '下次开启服务时生效。'}>
    <div className="browser-port">
      <input aria-label="端口号" inputMode="numeric" autoComplete="off" maxLength={5} placeholder="自动分配" value={draft} disabled={disabled} readOnly={pending} aria-busy={pending || undefined} aria-invalid={!valid || failed} aria-describedby={!valid || failed ? 'browser-port-error' : undefined}
        onChange={event => { attempted.current = null; setFailed(false); setDraft(event.target.value) }}
        onBlur={() => { void commit() }} onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void commit(true) } }} />
    </div>
  </SettingRow>
}
