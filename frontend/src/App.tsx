import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { AudioLines, ChevronRight, CircleHelp, Gamepad2, Headphones, Info, LoaderCircle, Moon, RefreshCw, Settings2, SlidersHorizontal, Sun, ArrowRightLeft, X, TriangleAlert, Check } from 'lucide-react'
import type { Device, Page, PreferenceKey, UiGateway, Scenario, Snapshot } from './data/types'
import { Button } from './components/ui/button'
import { Devices } from './components/Devices'
import { Automation } from './components/Automation'
import { Settings } from './components/Settings'
import { DeviceSheet } from './components/DeviceSheet'
import { BackupSheet } from './components/BackupSheet'
import { MaintenanceSheet } from './components/MaintenanceSheet'
import { OperationFailure } from './data/desktop'

const pages = {
  devices: { title: '声音设备', Icon: Headphones },
  automation: { title: '自动切换', Icon: ArrowRightLeft },
  settings: { title: '应用设置', Icon: Settings2 },
}
type Notice = { text: string; error?: boolean } | null

export function App({ gateway }: { gateway: UiGateway }) {
  const desktop = gateway.mode === 'desktop'
  const [page, setPage] = useState<Page>('devices')
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState<Notice>(null)
  const [dismissedWarning, setDismissedWarning] = useState('')
  const [editing, setEditing] = useState<Device | null>(null)
  const [backup, setBackup] = useState<'import' | 'export' | null>(null)
  const [maintenance, setMaintenance] = useState<'update' | 'files' | null>(null)
  const [scenario, setScenario] = useState<Scenario>('normal')
  const [dark, setDark] = useState(true)
  const [uncertain, setUncertain] = useState(false)
  const readOnly = desktop && (!snapshot?.CanWrite || uncertain)
  const locked = useRef(false)
  const revision = useRef(0)
  const main = useRef<HTMLElement>(null)
  const currentPage = pages[page]

  useEffect(() => { document.documentElement.classList.toggle('dark', dark); document.documentElement.style.colorScheme = dark ? 'dark' : 'light' }, [dark])
  useLayoutEffect(() => { if (main.current) main.current.scrollTop = 0 }, [page])
  useEffect(() => {
    const ticket = ++revision.current
    locked.current = true
    gateway.read().then(data => { if (ticket === revision.current) { setSnapshot(data); setDark(data.Preferences.DarkMode); setLoading(false); locked.current = false } }).catch(e => { if (ticket === revision.current) { setSnapshot(null); setError(String(e.message)); setLoading(false); locked.current = false } })
    return () => { revision.current++ }
  }, [gateway])

  const run = useCallback(async (operation: () => Promise<Snapshot>, message: string): Promise<boolean> => {
    if (locked.current) return false
    locked.current = true; setBusy(true)
    try {
      const data = await operation()
      setSnapshot(data); setDark(data.Preferences.DarkMode)
      setNotice({ text: message }); return true
    } catch (e) {
      if (e instanceof OperationFailure) {
        if (e.snapshot) { setSnapshot(e.snapshot); setDark(e.snapshot.Preferences.DarkMode) }
        if (e.requiresRefresh) setUncertain(true)
      }
      setNotice({ text: e instanceof Error ? e.message : '操作未完成，请重试。', error: true }); return false
    }
    finally { locked.current = false; setBusy(false) }
  }, [])

  async function loadScenario(next: Scenario) {
    if (gateway.mode !== 'preview' || locked.current || loading) return
    locked.current = true; setLoading(true); setError(''); setSnapshot(null); setNotice(null); setEditing(null); setScenario(next)
    try { const data = await gateway.setScenario(next); setSnapshot(data); setDark(data.Preferences.DarkMode) }
    catch (e) { setError(e instanceof Error ? e.message : '无法读取示例数据。') }
    finally { locked.current = false; setLoading(false) }
  }
  const refresh = useCallback(async () => {
    if (locked.current) return
    locked.current = true; setLoading(true); setError(''); setNotice(null); setEditing(null)
    try { const data = await gateway.read(); setSnapshot(data); setDark(data.Preferences.DarkMode); setUncertain(false) }
    catch (e) { setSnapshot(null); setError(e instanceof Error ? e.message : '无法读取设备状态。') }
    finally { locked.current = false; setLoading(false) }
  }, [gateway])
  useEffect(() => {
    if (!desktop || editing || backup || maintenance || uncertain) return
    const focused = () => { if (document.visibilityState !== 'hidden') void refresh() }
    window.addEventListener('focus', focused)
    return () => window.removeEventListener('focus', focused)
  }, [desktop, editing, backup, maintenance, uncertain, refresh])
  const preference = (key: PreferenceKey, value: boolean) => {
    if (readOnly) { if (key === 'DarkMode') setDark(value); return }
    void run(() => gateway.setPreference(key, value), desktop ? '设置已保存。' : '已更新预览设置；系统设置未改变。')
  }


  return <div className="app-shell">
    <a href="#main-content" className="skip-link">跳到主要内容</a>
    <aside className="sidebar">
      <div className="brand"><img src="/mark.svg" alt="" width="34" height="34" /><div><strong>声间</strong><span>AUDIO SWITCH</span></div></div>
      <div className="sidebar-label">工作空间</div>
      <nav aria-label="主导航">{(Object.keys(pages) as Page[]).map(key => { const { title, Icon } = pages[key]; return <button key={key} aria-label={title} title={title} aria-current={page === key ? 'page' : undefined} onClick={() => { setPage(key); setNotice(null) }}><Icon size={17} /><span>{title}</span>{page === key && <ChevronRight size={13} />}</button> })}</nav>
      <div className="sidebar-bottom">
        <div className="automation-hint"><span><ArrowRightLeft size={15} />自动切换</span><strong>{snapshot ? snapshot.Preferences.UseDevicePriority ? '按设备优先级选择' : '当前已关闭优先级' : desktop ? '等待读取设备' : '等待示例数据'}</strong><p>优先使用排序靠前的在线设备</p><button onClick={() => setPage('automation')}>管理规则 <ChevronRight size={12} /></button></div>
        <div className="sidebar-meta"><span>声间 <span className="version">v0.11.1</span></span><Button variant="ghost" size="icon" aria-label="关于此预览" onClick={() => setNotice({ text: desktop ? '桌面面板：操作交给已运行的声间后台处理；不会自动启动后台。' : '当前为浏览器模拟预览，不连接后台。' })}><CircleHelp size={15} /></Button></div>
      </div>
    </aside>
    <div className="workspace">
      <header className="topbar"><div className="breadcrumb"><AudioLines size={15} /><span>工作空间</span><ChevronRight size={12} /><strong>{currentPage.title}</strong></div><div className="topbar-actions">{snapshot?.Preferences.GameMode && <span className="game-badge"><Gamepad2 size={13} />游戏模式</span>}<Button variant="ghost" size="icon" aria-label={dark ? '切换到浅色模式' : '切换到深色模式'} disabled={busy || loading || !snapshot} onClick={() => preference('DarkMode', !dark)}>{dark ? <Sun /> : <Moon />}</Button></div></header>
      <div className="preview-banner"><Info size={14} /><span>{desktop ? uncertain ? '操作结果尚未确认，请先刷新状态' : readOnly ? '只读连接，仅显示真实状态，不会改变系统设置' : '已连接真实后台，操作会改变系统设置' : '界面预览，操作不会改变系统设置'}</span><span className="preview-detail">{desktop ? readOnly ? '需要支持第三阶段操作的后台' : '预设仅保存，切换时应用' : '所有设备均为示例'}</span></div>
      <main ref={main} id="main-content" tabIndex={-1} className="main-content">
        <div className="page-heading"><h1>{currentPage.title}</h1>{page === 'devices' && <Button variant="outline" disabled={busy || loading} onClick={() => { void (scenario === 'error' ? loadScenario('normal') : refresh()) }}><RefreshCw size={14} className={loading ? 'spin' : ''} /> {desktop ? '刷新状态' : '刷新示例'}</Button>}</div>
        {loading ? <div className="loading-state" role="status"><LoaderCircle className="spin" size={26} /><h2>{desktop ? '正在读取设备状态…' : '正在载入示例设备…'}</h2><p>{desktop ? '读取已运行后台的状态，不会启动后台。' : '不会连接真实音频后台。'}</p><div className="skeleton-row" /><div className="skeleton-row" /></div> : error ? <div className="error-state" role="alert"><TriangleAlert size={28} /><h2>暂时无法显示设备</h2><p>{error}</p><Button variant="outline" onClick={() => { void (desktop ? refresh() : loadScenario('normal')) }}><RefreshCw />重试</Button></div> : snapshot && <>
          {desktop && snapshot.BackendWarning && snapshot.BackendWarning !== dismissedWarning && <div className="backend-warning" role="status"><span>后台提示：{snapshot.BackendWarning}</span><Button variant="ghost" size="icon" aria-label="关闭后台提示" onClick={() => setDismissedWarning(snapshot.BackendWarning ?? '')}><X /></Button></div>}
          {page === 'devices' && <Devices snapshot={snapshot} busy={busy} readOnly={readOnly} onSwitch={device => { void run(() => gateway.switchDevice(device.Id), desktop ? `已切换到「${device.Name}」。音效结果请查看后台提示。` : `已在预览中切换到「${device.Name}」。系统设备未改变。`) }} onSettings={device => { setNotice(null); setEditing(device) }} onAutomation={() => setPage('automation')} />}
          {page === 'automation' && <Automation desktop={desktop} snapshot={snapshot} busy={busy} readOnly={readOnly} onPreference={preference} onReorder={(flow, ids) => { void run(() => gateway.reorder(flow, ids), desktop ? '排序已保存；已开启优先级时后台会立即选择设备。' : '已调整示例优先级，离线设备仍保留。') }} />}
          {page === 'settings' && <Settings desktop={desktop} snapshot={{ ...snapshot, Preferences: { ...snapshot.Preferences, DarkMode: dark } }} busy={busy} readOnly={readOnly} onPreference={preference} onStartup={value => { void run(() => gateway.setStartup(value), desktop ? 'Windows 自启设置已更新。' : '已更新自启开关预览；没有修改 Windows 启动项。') }} onBackup={mode => { setNotice(null); setBackup(mode) }} onMaintenance={kind => { setNotice(null); setMaintenance(kind) }} />}
        </>}
      </main>
      <footer className="statusbar"><span><span className={error ? 'offline-dot' : 'online-dot'} />{desktop ? busy ? '正在处理操作…' : snapshot?.DolbyApplying ? '正在应用 Dolby，刷新可查看结果' : loading ? '正在读取…' : error ? '连接不可用 · 点击重试' : '真实状态 · 手动刷新或返回窗口更新' : busy ? '正在更新预览…' : '模拟数据 · 仅本次会话'}</span>{!desktop && <label><SlidersHorizontal size={12} /><span>预览场景</span><select aria-label="预览场景" value={scenario} disabled={busy || loading} onChange={e => { void loadScenario(e.target.value as Scenario) }}><option value="normal">常规设备</option><option value="empty">空设备</option><option value="duplicate">同名设备</option><option value="long">长名称</option><option value="loading">加载中</option><option value="error">连接错误</option></select></label>}</footer>
    </div>
    {notice && <div className={`toast ${notice.error ? 'toast-error' : ''}`} role={notice.error ? 'alert' : 'status'}>{notice.error ? <TriangleAlert size={17} /> : <Check size={17} />}<span>{notice.text}</span><Button variant="ghost" size="icon" aria-label="关闭操作提示" onClick={() => setNotice(null)}><X /></Button></div>}
    {editing && snapshot && <DeviceSheet onDolbyResult={(text, error) => setNotice({ text, error })} onDolbySaved={data => { setSnapshot(data); setNotice({ text: desktop ? 'Dolby 方案已保存。应用结果请查看 Dolby 编辑器。' : 'Dolby 示例方案已保存；系统设置未改变。' }) }} onDolbyFailure={failure => { setNotice({ text:failure.message, error:true }); if (failure.snapshot) setSnapshot(failure.snapshot); if (failure.requiresRefresh) setUncertain(true) }} key={editing.Id} device={editing} snapshot={snapshot} busy={busy} readOnly={readOnly} gateway={gateway} saveError={notice?.error ? notice.text : undefined} onClose={() => setEditing(null)} onSave={(profile, rule, expectedProfile, expectedRule) => run(() => gateway.saveDevice(editing.Id, profile, rule, expectedProfile, expectedRule), desktop ? '预设已保存；没有切换设备或应用音效。' : '预设已保存在本次预览中；没有切换设备或应用音效。')} />}
    {backup && <BackupSheet mode={backup} gateway={gateway} onClose={() => setBackup(null)} onResult={data => { setSnapshot(data); setDark(data.Preferences.DarkMode) }} onFailure={failure => {
      if (failure.snapshot) { setSnapshot(failure.snapshot); setDark(failure.snapshot.Preferences.DarkMode) }
      if (failure.requiresRefresh) setUncertain(true)
    }} />}
    {maintenance && snapshot && <MaintenanceSheet kind={maintenance} gateway={gateway} snapshot={snapshot} onClose={() => setMaintenance(null)} />}
  </div>
}
