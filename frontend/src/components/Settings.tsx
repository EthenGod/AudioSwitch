import { Download, Upload, RefreshCw, ShieldCheck, ArrowUpRight, Moon, Sun } from 'lucide-react'
import type { PreferenceKey, Snapshot } from '@/data/types'
import { SettingRow, ToggleRow } from './shared'
import { APP_VERSION } from '../version'

export function Settings({ snapshot, busy, readOnly = false, desktop = false, onPreference, onStartup, onBackup, onMaintenance }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; desktop?: boolean; onPreference: (key: PreferenceKey, value: boolean) => void
  onStartup: (value: boolean) => void;
  onBackup: (mode: 'import' | 'export') => void
  onMaintenance: (kind: 'update' | 'files') => void
}) {
  const prefs = snapshot.Preferences
  return <>
    <section className="settings-group" aria-label="应用偏好">
      <SettingRow title="外观"><div className="segmented"><button aria-label="浅色模式" aria-pressed={!prefs.DarkMode} disabled={busy} onClick={() => onPreference('DarkMode', false)}><Sun size={14} />浅色</button><button aria-label="深色模式" aria-pressed={prefs.DarkMode} disabled={busy} onClick={() => onPreference('DarkMode', true)}><Moon size={14} />深色</button></div></SettingRow>
      <ToggleRow title="游戏模式" description="暂停后台自动更新。" value={prefs.GameMode} disabled={busy || readOnly} onChange={v => onPreference('GameMode', v)} />
      <ToggleRow title="开机自启" description={desktop ? snapshot.StartupMessage || (snapshot.StartupAvailable && !readOnly ? '登录 Windows 后只启动托盘。' : '当前连接无法修改自启设置。') : '登录 Windows 后启动托盘。'} value={snapshot.StartupEnabled} disabled={busy || readOnly || (desktop && !snapshot.StartupAvailable)} onChange={onStartup} />
      <ToggleRow title="自动更新" description="空闲时下载，下次启动安装；游戏模式下暂缓。" value={prefs.AutoUpdateEnabled} disabled={busy || readOnly} onChange={v => onPreference('AutoUpdateEnabled', v)} />
    </section>
    <div className="section-heading"><h2>备份与维护</h2><span className="section-caption">{desktop && !snapshot.CanManageBackup ? '导入导出需要新版后台' : ''}</span></div>
    <section className="maintenance-grid">{[
      { title: '导出备份', Icon: Upload },
      { title: '导入设置', Icon: Download },
      { title: '检查更新', Icon: RefreshCw },
      { title: '文件检查', Icon: ShieldCheck },
    ].map(({ title, Icon }, index) => <button className="maintenance-item" key={title} disabled={busy || (desktop && (index < 2 ? readOnly || !snapshot.CanManageBackup : !snapshot.CanCheckMaintenance))} onClick={() => index < 2 ? onBackup(index === 0 ? 'export' : 'import') : onMaintenance(index === 2 ? 'update' : 'files')}><Icon size={19} /><div><strong>{title}</strong></div><ArrowUpRight size={15} /></button>)}</section>
    <div className="about-line"><img src="/mark.svg" width="30" height="30" alt="" /><div><strong>声间 <span>Audio Switch</span></strong><p>v{APP_VERSION} {desktop ? readOnly ? '兼容只读模式' : '桌面版' : '界面预览'}</p></div></div>
  </>
}
