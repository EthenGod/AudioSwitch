import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Gamepad2, Headphones, Info, LoaderCircle, Moon, RefreshCw, Settings2, SlidersHorizontal, Sun, ArrowRightLeft, X, TriangleAlert, Check } from 'lucide-react'
import type { Device, Page, PreferenceKey, UiGateway, Scenario, Snapshot } from './data/types'
import { Button } from './components/ui/button'
import { Select } from './components/ui/select'
import { Devices } from './components/Devices'
import { Automation } from './components/Automation'
import { Settings } from './components/Settings'
import { DeviceSheet } from './components/DeviceSheet'
import { BackupSheet } from './components/BackupSheet'
import { MaintenanceSheet } from './components/MaintenanceSheet'
import { UpgradeSheet } from './components/UpgradeSheet'
import { OperationFailure } from './data/desktop'
import { APP_VERSION } from './version'
import { PageTransition } from './components/PageTransition'
import { ActionSlot } from './components/ActionSlot'
import { PanelAlert } from './components/PanelAlert'

const pages = {
  devices: { title: '声音设备', Icon: Headphones },
  automation: { title: '自动切换', Icon: ArrowRightLeft },
  settings: { title: '应用设置', Icon: Settings2 },
}
type Notice = { text: string; error?: boolean } | null

export function App({ gateway, onReady }: { gateway: UiGateway; onReady?: (dark: boolean) => void }) {
  const desktop = gateway.mode === 'desktop'
  useEffect(() => {
    document.title = desktop ? '声间 设备面板' : '声间 界面预览'
    document.querySelector('meta[name="description"]')?.setAttribute('content', desktop
      ? '声间 Audio Switch 音频设备管理'
      : '声间 Audio Switch 仅使用模拟数据的界面预览')
  }, [desktop])
  const [page, setPage] = useState<Page>('devices')
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState<Notice>(null)
  const [dismissedWarning, setDismissedWarning] = useState('')
  const [editing, setEditing] = useState<Device | null>(null)
  const [backup, setBackup] = useState<'import' | 'export' | null>(null)
  const [maintenance, setMaintenance] = useState<'update' | 'files' | null>(null)
  const [scenario, setScenario] = useState<Scenario>('normal')
  const [dark, setDark] = useState(true)
  const [uncertain, setUncertain] = useState(false)
  const readOnly = desktop && (!snapshot?.CanWrite || uncertain || !!error)
  const locked = useRef(false)
  const revision = useRef(0)
  const main = useRef<HTMLElement>(null)
  const currentPage = pages[page]

  useLayoutEffect(() => { document.documentElement.classList.toggle('dark', dark); document.documentElement.style.colorScheme = dark ? 'dark' : 'light' }, [dark])
  const presented = useRef(false)
  useEffect(() => {
    if (loading || !onReady || presented.current) return
    // Commit themed content before allowing the native window to appear.
    const timer = setTimeout(() => { presented.current = true; onReady(dark) }, 0)
    return () => clearTimeout(timer)
  }, [loading, dark, onReady])
  useLayoutEffect(() => { if (main.current) main.current.scrollTop = 0 }, [page])
  useEffect(() => {
    const ticket = ++revision.current
    locked.current = true
    gateway.read().then(data => { if (ticket === revision.current) { setSnapshot(data); setDark(data.Preferences.DarkMode); setLoading(false); locked.current = false } }).catch(e => { if (ticket === revision.current) { setSnapshot(null); setError(String(e.message)); setLoading(false); locked.current = false } })
    return () => { revision.current++ }
  }, [gateway])

  const run = useCallback(async (operation: () => Promise<Snapshot>, message: string | null): Promise<boolean> => {
    if (locked.current) return false
    locked.current = true; setBusy(true)
    try {
      const data = await operation()
      setSnapshot(data); setDark(data.Preferences.DarkMode)
      setNotice(message ? { text: message } : null); return true
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
    locked.current = true; setRefreshing(true)
    try { const data = await gateway.read(); setSnapshot(data); setDark(data.Preferences.DarkMode); setUncertain(false); setError('') }
    catch (e) { setError(e instanceof Error ? e.message : '无法读取设备状态。') }
    finally { locked.current = false; setLoading(false); setRefreshing(false) }
  }, [gateway])
  useEffect(() => {
    if (!desktop || editing || backup || maintenance || uncertain) return
    const focused = () => { if (document.visibilityState !== 'hidden') void refresh() }
    window.addEventListener('focus', focused)
    return () => window.removeEventListener('focus', focused)
  }, [desktop, editing, backup, maintenance, uncertain, refresh])
  const preference = (key: PreferenceKey, value: boolean) => {
    if (readOnly) { if (key === 'DarkMode') setDark(value); return }
    void run(() => gateway.setPreference(key, value), null)
  }


  return <div className="app-shell">
    <a href="#main-content" className="skip-link">跳到主要内容</a>
    <aside className="sidebar">
      <div className="brand"><img src="/mark.svg" alt="" width="34" height="34" /><div><strong>声间</strong></div></div>
      <nav aria-label="主导航">{(Object.keys(pages) as Page[]).map(key => { const { title, Icon } = pages[key]; return <button key={key} aria-label={title} title={title} aria-current={page === key ? 'page' : undefined} onClick={() => { setPage(key); setNotice(null) }}><Icon size={17} /><span>{title}</span></button> })}</nav>
      <div className="sidebar-bottom">
        <div className="sidebar-meta"><span>声间 <span className="version">v{APP_VERSION}</span></span></div>
      </div>
    </aside>
    <div className="workspace">
      {(!desktop || readOnly) && <div className="preview-banner"><Info size={14} /><span>{desktop ? error ? '连接中断，仅显示上次状态' : uncertain ? '操作结果尚未确认，请先刷新状态' : readOnly ? '只读连接，仅显示真实状态，不会改变系统设置' : '已连接设备' : '界面预览，操作不会改变系统设置'}</span></div>}
      <main ref={main} id="main-content" tabIndex={-1} className="main-content">
        <div className="page-heading"><h1>{currentPage.title}</h1><div className="page-actions">
          <ActionSlot visible={page === 'devices'}><Button variant="outline" pending={busy || refreshing} disabled={loading} onClick={() => { void (scenario === 'error' ? loadScenario('normal') : refresh()) }}><RefreshCw size={14} className={loading || refreshing ? 'spin' : ''} /> {desktop ? '刷新状态' : '刷新示例'}</Button></ActionSlot>
          <ActionSlot visible={!!snapshot?.Preferences.GameMode}><span className="game-badge"><Gamepad2 size={13} />游戏模式</span></ActionSlot>
          <ActionSlot visible={page !== 'settings'}><Button variant="ghost" size="icon" aria-label={dark ? '切换到浅色模式' : '切换到深色模式'} pending={busy} disabled={loading || !snapshot} onClick={() => preference('DarkMode', !dark)}>{dark ? <Sun /> : <Moon />}</Button></ActionSlot>
        </div></div>
        <PanelAlert message={error}><Button variant="outline" pending={refreshing || loading} onClick={() => { void (desktop ? refresh() : loadScenario('normal')) }}><RefreshCw size={14} className={refreshing || loading ? 'spin' : ''} />重试</Button></PanelAlert>
        <PanelAlert warning message={desktop && snapshot?.BackendWarning && snapshot.BackendWarning !== dismissedWarning ? snapshot.BackendWarning : ''}><Button variant="ghost" size="icon" aria-label="关闭后台提示" onClick={() => setDismissedWarning(snapshot?.BackendWarning ?? '')}><X /></Button></PanelAlert>
        <PageTransition page={page}>
        {loading ? <div className="loading-state" role="status"><LoaderCircle className="spin" size={26} /><h2>{desktop ? '正在读取设备状态…' : '正在载入示例设备…'}</h2><div className="skeleton-row" /><div className="skeleton-row" /></div> : snapshot && <>
          {page === 'devices' && <Devices snapshot={snapshot} busy={busy} readOnly={readOnly} onSwitch={device => { void run(() => gateway.switchDevice(device.Id), desktop ? `已切换到「${device.Name}」。` : `已在预览中切换到「${device.Name}」。`) }} onSettings={device => { setNotice(null); setEditing(device) }} onAutomation={() => setPage('automation')} />}
          {page === 'automation' && <Automation desktop={desktop} snapshot={snapshot} busy={busy} readOnly={readOnly} onPreference={preference} onReorder={(flow, ids) => run(() => gateway.reorder(flow, ids), null)} />}
          {page === 'settings' && <Settings onBrowserPort={port => run(() => gateway.setBrowserPort(port), null)} desktop={desktop} snapshot={{ ...snapshot, Preferences: { ...snapshot.Preferences, DarkMode: dark } }} busy={busy} readOnly={readOnly} onPreference={preference} onStartup={value => { void run(() => gateway.setStartup(value), null) }} onBackup={mode => { setNotice(null); setBackup(mode) }} onMaintenance={kind => { setNotice(null); setMaintenance(kind) }} />}
        </>}
        </PageTransition>
      </main>
      <footer className="statusbar"><span><span className={error ? 'offline-dot' : 'online-dot'} />{desktop ? busy ? '正在处理操作…' : snapshot?.DolbyApplying ? '正在应用 Dolby，刷新可查看结果' : loading || refreshing ? '正在读取…' : error ? '连接不可用，请重试' : '已连接' : busy ? '正在更新预览…' : '模拟数据'}</span>{!desktop && <div className="preview-scenario"><SlidersHorizontal size={12} /><span>预览场景</span><Select label="预览场景" value={scenario} pending={busy} disabled={loading} onValueChange={value => { void loadScenario(value as Scenario) }} options={[{ value: 'normal', label: '常规设备' }, { value: 'empty', label: '空设备' }, { value: 'duplicate', label: '同名设备' }, { value: 'long', label: '长名称' }, { value: 'loading', label: '加载中' }, { value: 'error', label: '连接错误' }]} /></div>}</footer>
    </div>
    {notice && <div className={`toast ${notice.error ? 'toast-error' : ''}`} role={notice.error ? 'alert' : 'status'}>{notice.error ? <TriangleAlert size={17} /> : <Check size={17} />}<span>{notice.text}</span><Button variant="ghost" size="icon" aria-label="关闭操作提示" onClick={() => setNotice(null)}><X /></Button></div>}
    {editing && snapshot && <DeviceSheet onDolbyResult={(text, error) => setNotice({ text, error })} onDolbySaved={data => { setSnapshot(data); setNotice({ text: desktop ? 'Dolby 方案已保存。' : 'Dolby 示例方案已保存。' }) }} onDolbyFailure={failure => { setNotice({ text:failure.message, error:true }); if (failure.snapshot) setSnapshot(failure.snapshot); if (failure.requiresRefresh) setUncertain(true) }} key={editing.Id} device={editing} snapshot={snapshot} busy={busy} readOnly={readOnly} gateway={gateway} saveError={notice?.error ? notice.text : undefined} onClose={() => setEditing(null)} onSave={(profile, rule, expectedProfile, expectedRule) => run(() => gateway.saveDevice(editing.Id, profile, rule, expectedProfile, expectedRule), desktop ? '预设已保存。' : '预设已保存在本次预览中。')} />}
    {backup && <BackupSheet mode={backup} gateway={gateway} onClose={() => setBackup(null)} onResult={data => { setSnapshot(data); setDark(data.Preferences.DarkMode) }} onFailure={failure => {
      if (failure.snapshot) { setSnapshot(failure.snapshot); setDark(failure.snapshot.Preferences.DarkMode) }
      if (failure.requiresRefresh) setUncertain(true)
    }} />}
    {maintenance && snapshot && (!desktop || snapshot.CanManageUpdates ? <UpgradeSheet preferenceBusy={busy || readOnly} onPreference={async value => { if (!await run(() => gateway.setPreference('IncludePrerelease', value), null)) throw new Error('更新设置未保存，请重试。') }} kind={maintenance === 'files' ? 'repair' : 'update'} gateway={gateway} snapshot={snapshot} onClose={() => setMaintenance(null)} /> : <MaintenanceSheet kind={maintenance} gateway={gateway} snapshot={snapshot} onClose={() => setMaintenance(null)} />)}
  </div>
}
