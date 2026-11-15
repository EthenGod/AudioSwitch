import { t, resolveLanguage } from './i18n'
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Gamepad2, Headphones, Info, LoaderCircle, Moon, RefreshCw, Settings2, SlidersHorizontal, Sun, ArrowRightLeft, X } from 'lucide-react'
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
import { ConnectionError } from './components/ConnectionError'
import { useAppearance } from './useAppearance'
import { ScrollSurface } from './components/ScrollSurface'
import { Toast, type Notice } from './components/Toast'
import { useLanguage } from './i18n/useLanguage'
import { LanguageDialog } from './components/LanguageDialog'

const pages = {
  devices: { get title() { return t('声音设备') }, Icon: Headphones },
  automation: { get title() { return t('自动切换') }, Icon: ArrowRightLeft },
  settings: { get title() { return t('应用设置') }, Icon: Settings2 },
}

export function App({ gateway, onReady }: { gateway: UiGateway; onReady?: (dark: boolean, system?: boolean) => void }) {
  const desktop = gateway.mode === 'desktop'
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const language = useLanguage(snapshot?.Preferences.Language, snapshot?.ResolvedLanguage, !!snapshot)
  useEffect(() => {
    document.title = desktop ? t("声间 设备面板") : t("声间 界面预览")
    document.querySelector('meta[name="description"]')?.setAttribute('content', desktop
      ? t("声间 Audio Switch 音频设备管理")
      : t("声间 Audio Switch 仅使用模拟数据的界面预览"))
  }, [desktop, language])
  const [page, setPage] = useState<Page>('devices')
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
  const [preferredDark, setDark] = useState(true)
  const connectionFailed = !!error && !snapshot
  const dark = useAppearance(connectionFailed ? undefined : preferredDark, snapshot?.Preferences.UiStyle, !loading)
  const [uncertain, setUncertain] = useState(false)
  const readOnly = desktop && (!snapshot?.CanWrite || uncertain || !!error)
  const locked = useRef(false)
  const revision = useRef(0)
  const main = useRef<HTMLElement>(null)
  const currentPage = pages[page]

  useEffect(() => {
    if (loading || !onReady) return
    // Commit themed content before allowing the native window to appear.
    const timer = setTimeout(() => { if (connectionFailed) onReady(dark, true); else onReady(dark) }, 0)
    return () => clearTimeout(timer)
  }, [loading, dark, connectionFailed, onReady])
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
      setNotice({ text: e instanceof Error ? e.message : t("操作未完成，请重试"), error: true }); return false
    }
    finally { locked.current = false; setBusy(false) }
  }, [])

  async function loadScenario(next: Scenario) {
    if (gateway.mode !== 'preview' || locked.current || loading) return
    locked.current = true; setLoading(true); setError(''); setSnapshot(null); setNotice(null); setEditing(null); setScenario(next)
    try { const data = await gateway.setScenario(next); setSnapshot(data); setDark(data.Preferences.DarkMode) }
    catch (e) { setError(e instanceof Error ? e.message : t("无法读取示例数据")) }
    finally { locked.current = false; setLoading(false) }
  }
  const refresh = useCallback(async () => {
    if (locked.current) return
    locked.current = true; setRefreshing(true)
    try { const data = await gateway.read(); setSnapshot(data); setDark(data.Preferences.DarkMode); setUncertain(false); setError('') }
    catch (e) { setError(e instanceof Error ? e.message : t("无法读取设备状态")) }
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
    <a href="#main-content" className="skip-link">{t("跳到主要内容")}</a>
    <aside className="sidebar">
      <div className="brand"><img src="/mark.svg" alt="" width="34" height="34" /><div><strong>{t("声间")}</strong></div></div>
      <nav aria-label={t("主导航")}>{(Object.keys(pages) as Page[]).map(key => { const { title, Icon } = pages[key]; return <button key={key} aria-label={title} title={title} aria-current={page === key ? 'page' : undefined} onClick={() => { setPage(key); setNotice(null) }}><Icon size={17} /><span>{title}</span></button> })}</nav>
      <div className="sidebar-bottom">
        <div className="sidebar-meta"><span>{t("声间")}<span className="version">v{APP_VERSION}</span></span></div>
      </div>
    </aside>
    <div className="workspace">
      {(!desktop || (readOnly && snapshot)) && <div className="preview-banner"><Info size={14} /><span>{desktop ? error ? t("连接中断，仅显示上次状态") : uncertain ? t("操作结果尚未确认，请先刷新状态") : t("只读连接，仅显示真实状态，不会改变系统设置") : t("界面预览，操作不会改变系统设置")}</span></div>}
      <ScrollSurface main viewportRef={main} id="main-content" className="main-content" bodyClassName="main-scroll-body">
        {!connectionFailed && <div className="page-heading"><h1>{currentPage.title}</h1><div className="page-actions">
          <ActionSlot visible={page === 'devices'}><Button variant="outline" pending={busy || refreshing} disabled={loading} onClick={() => { void (scenario === 'error' ? loadScenario('normal') : refresh()) }}><RefreshCw size={14} className={loading || refreshing ? 'spin' : ''} /> {desktop ? t("刷新状态") : t("刷新示例")}</Button></ActionSlot>
          <ActionSlot visible={!!snapshot?.Preferences.GameMode}><span className="game-badge"><Gamepad2 size={13} />{t("游戏模式")}</span></ActionSlot>
          <ActionSlot visible={page !== 'settings'}><Button variant="ghost" size="icon" aria-label={dark ? t("切换到浅色模式") : t("切换到深色模式")} pending={busy} disabled={loading || !snapshot} onClick={() => preference('DarkMode', !dark)}>{dark ? <Sun /> : <Moon />}</Button></ActionSlot>
        </div></div>}
        {connectionFailed && <ConnectionError title={t("无法读取设备状态")} message={error} busy={refreshing || loading} retry={() => { void (desktop ? refresh() : loadScenario('normal')) }} />}
        <PanelAlert message={snapshot ? error : ''}><Button variant="outline" pending={refreshing || loading} onClick={() => { void (desktop ? refresh() : loadScenario('normal')) }}><RefreshCw size={14} className={refreshing || loading ? 'spin' : ''} />{t("重试")}</Button></PanelAlert>
        <PanelAlert warning message={desktop && snapshot?.BackendWarning && snapshot.BackendWarning !== dismissedWarning ? snapshot.BackendWarning : ''}><Button variant="ghost" size="icon" aria-label={t("关闭后台提示")} onClick={() => setDismissedWarning(snapshot?.BackendWarning ?? '')}><X /></Button></PanelAlert>
        <PageTransition page={page}>
        {loading ? <div className="loading-state" role="status"><LoaderCircle className="spin" size={26} /><h2>{desktop ? t("正在读取设备状态…") : t("正在载入示例设备…")}</h2><div className="skeleton-row" /><div className="skeleton-row" /></div> : snapshot && <>
          {page === 'devices' && <Devices snapshot={snapshot} busy={busy} readOnly={readOnly} onSwitch={device => { void run(() => gateway.switchDevice(device.Id), desktop ? t("已切换到「{0}」", device.Name) : t("已在预览中切换到「{0}」", device.Name)) }} onSettings={device => { setNotice(null); setEditing(device) }} onAutomation={() => setPage('automation')} />}
          {page === 'automation' && <Automation desktop={desktop} snapshot={snapshot} busy={busy} readOnly={readOnly} onPreference={preference} onReorder={(flow, ids) => run(() => gateway.reorder(flow, ids), null)} />}
          {page === 'settings' && <Settings onLanguage={value => { void run(() => gateway.setLanguage(value), null) }} onUiStyle={style => { void run(() => gateway.setUiStyle(style), null) }} onBrowserPort={port => run(() => gateway.setBrowserPort(port), null)} desktop={desktop} snapshot={{ ...snapshot, Preferences: { ...snapshot.Preferences, DarkMode: dark } }} busy={busy} readOnly={readOnly} onPreference={preference} onStartup={value => { void run(() => gateway.setStartup(value), null) }} onBackup={mode => { setNotice(null); setBackup(mode) }} onMaintenance={kind => { setNotice(null); setMaintenance(kind) }} />}
        </>}
        </PageTransition>
      </ScrollSurface>
      <footer className="statusbar"><span><span className={error ? 'offline-dot' : 'online-dot'} />{desktop ? busy ? t("正在处理操作…") : snapshot?.DolbyApplying ? t("正在应用 Dolby，刷新可查看结果") : loading || refreshing ? t("正在读取…") : error ? t("连接不可用，请重试") : t("已连接") : busy ? t("正在更新预览…") : t("模拟数据")}</span>{!desktop && <div className="preview-scenario"><SlidersHorizontal size={12} /><span>{t("预览场景")}</span><Select label={t("预览场景")} value={scenario} pending={busy} disabled={loading} onValueChange={value => { void loadScenario(value as Scenario) }} options={[{ value: 'normal', label: t("常规设备") }, { value: 'empty', label: t("空设备") }, { value: 'duplicate', label: t("同名设备") }, { value: 'long', label: t("长名称") }, { value: 'loading', label: t("加载中") }, { value: 'error', label: t("连接错误") }]} /></div>}</footer>
    </div>
    <Toast notice={notice} onClose={() => setNotice(null)} />
    {snapshot && (!desktop || snapshot.CanSetLanguage) && <LanguageDialog open={snapshot.Preferences.Language === null} allowSystem={!desktop || snapshot.CanFollowSystemLanguage === true} recommended={resolveLanguage(null, snapshot.ResolvedLanguage)} onConfirm={value => run(() => gateway.setLanguage(value), null)} />}
    {editing && snapshot && <DeviceSheet onDolbyResult={(text, error) => setNotice({ text, error })} onDolbySaved={data => { setSnapshot(data); setNotice({ text: desktop ? t("Dolby 方案已保存") : t("Dolby 示例方案已保存") }) }} onDolbyFailure={failure => { setNotice({ text:failure.message, error:true }); if (failure.snapshot) setSnapshot(failure.snapshot); if (failure.requiresRefresh) setUncertain(true) }} key={editing.Id} device={editing} snapshot={snapshot} busy={busy} readOnly={readOnly} gateway={gateway} saveError={notice?.error ? notice.text : undefined} onClose={() => setEditing(null)} onSave={(profile, rule, expectedProfile, expectedRule, rename) => run(() => gateway.saveDevice(editing.Id, profile, rule, expectedProfile, expectedRule, rename), desktop ? t("预设已保存") : t("预设已保存在本次预览中"))} />}
    {backup && <BackupSheet mode={backup} gateway={gateway} onClose={() => setBackup(null)} onResult={data => { setSnapshot(data); setDark(data.Preferences.DarkMode) }} onFailure={failure => {
      if (failure.snapshot) { setSnapshot(failure.snapshot); setDark(failure.snapshot.Preferences.DarkMode) }
      if (failure.requiresRefresh) setUncertain(true)
    }} />}
    {maintenance && snapshot && (!desktop || snapshot.CanManageUpdates ? <UpgradeSheet preferenceBusy={busy || readOnly} onPreference={async value => { if (!await run(() => gateway.setPreference('IncludePrerelease', value), null)) throw new Error(t("更新设置未保存，请重试")) }} kind={maintenance === 'files' ? 'repair' : 'update'} gateway={gateway} snapshot={snapshot} onClose={() => setMaintenance(null)} /> : <MaintenanceSheet kind={maintenance} gateway={gateway} snapshot={snapshot} onClose={() => setMaintenance(null)} />)}
  </div>
}
