import { Download, Upload, RefreshCw, ShieldCheck, ArrowUpRight, Moon, Sun } from 'lucide-react'
import type { PreferenceKey, Snapshot } from '@/data/types'
import { Button } from './ui/button'
import { SettingRow, ToggleRow } from './shared'

export function Settings({ snapshot, busy, readOnly = false, onPreference, onStartup, onUnavailable }: {
  snapshot: Snapshot; busy: boolean; readOnly?: boolean; onPreference: (key: PreferenceKey, value: boolean) => void
  onStartup: (value: boolean) => void; onUnavailable: (name: string) => void
}) {
  const prefs = snapshot.Preferences
  return <>
    <section className="settings-group" aria-label="应用偏好">
      <SettingRow title="外观" description="选择舒适的显示方式。此处只预览，不保存到系统配置。"><div className="segmented"><button aria-label="浅色模式" aria-pressed={!prefs.DarkMode} disabled={busy} onClick={() => onPreference('DarkMode', false)}><Sun size={14} />浅色</button><button aria-label="深色模式" aria-pressed={prefs.DarkMode} disabled={busy} onClick={() => onPreference('DarkMode', true)}><Moon size={14} />深色</button></div></SettingRow>
      <ToggleRow title="游戏模式" description="正式版中暂停自动更新活动，减少游戏时的后台打扰。" value={prefs.GameMode} disabled={busy || readOnly} onChange={v => onPreference('GameMode', v)} />
      <ToggleRow title="开机自启" description={readOnly ? snapshot.StartupMessage || '显示后台读取的自启状态，本阶段不修改。' : '正式版中登录 Windows 后只启动托盘；原型不会修改启动项。'} value={snapshot.StartupEnabled} disabled={busy || readOnly} onChange={onStartup} />
      <ToggleRow title="自动更新" description="正式版中空闲时下载，下次启动安装；游戏模式下暂缓。" value={prefs.AutoUpdateEnabled} disabled={busy || readOnly} onChange={v => onPreference('AutoUpdateEnabled', v)} />
    </section>
    <div className="section-heading"><h2>备份与维护</h2><span className="section-caption">将在后续阶段接入</span></div>
    <section className="maintenance-grid">{[
      { title: '导出备份', description: '保留设备偏好与音效预设', Icon: Upload },
      { title: '导入设置', description: '从已有备份恢复配置', Icon: Download },
      { title: '检查更新', description: '查看版本说明与可用更新', Icon: RefreshCw },
      { title: '文件检查', description: '检查必要运行文件是否完整', Icon: ShieldCheck },
    ].map(({ title, description, Icon }) => <button className="maintenance-item" key={title} onClick={() => onUnavailable(title)}><Icon size={19} /><div><strong>{title}</strong><p>{description}</p><span>暂未接入</span></div><ArrowUpRight size={15} /></button>)}</section>
    <div className="about-line"><img src="/mark.svg" width="30" height="30" alt="" /><div><strong>声间 <span>Audio Switch</span></strong><p>v0.11.1 · {readOnly ? '第二阶段只读桌面版' : '界面预览'}</p></div><Button variant="ghost" size="sm" onClick={() => onUnavailable('Dolby 高级编辑器')}>Dolby 编辑器 · 暂未接入</Button></div>
  </>
}
