import { useEffect, useRef, useState } from 'react'
import { ArrowUpRight, AudioLines, Check, ChevronDown, Headphones, LoaderCircle, Mic, Unplug, X } from 'lucide-react'
import { Button } from './components/ui/button'
import { Select } from './components/ui/select'
import { DeviceIcon } from './components/shared'
import { ScrollSurface } from './components/ScrollSurface'
import { canRestore, PromptFailure, type PromptAction, type PromptGateway, type PromptState } from './data/prompt'

export function PromptApp({ gateway }: { gateway: PromptGateway }) {
  const [state, setState] = useState<PromptState>(), [token, setToken] = useState(''), [selected, setSelected] = useState('')
  const [error, setError] = useState(''), [busy, setBusy] = useState(false), [closed, setClosed] = useState(false)
  const lock = useRef(false), paused = useRef(false)
  const selection = useRef({ token: '', id: '' })
  const firstToken = useRef('')
  const [choosing, setChoosing] = useState(false)
  const lastExpansion = useRef(false)
  useEffect(() => {
    if (lastExpansion.current === choosing) return
    lastExpansion.current = choosing
    // A resize failure leaves the existing scrollable viewport usable.
    void gateway.resize?.(choosing, window.matchMedia('(prefers-reduced-motion: reduce)').matches).catch(() => {})
  }, [choosing, gateway])
  const [hoverAction, setHoverAction] = useState<'selected' | 'previous' | 'close' | null>(null)
  const [focusAction, setFocusAction] = useState<'selected' | 'previous' | 'close' | null>(null)
  const [phase, setPhase] = useState<'opening' | 'open' | 'closing'>('opening')
  const leaving = useRef(false), mounted = useRef(true)
  const exitWait = useRef<{ timer: ReturnType<typeof setTimeout>; finish: () => void } | null>(null)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      if (exitWait.current) { clearTimeout(exitWait.current.timer); exitWait.current.finish() }
    }
  }, [])
  async function close(openPanel = false) {
    if (!mounted.current || leaving.current) return
    leaving.current = true; paused.current = true; setPhase('closing')
    if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      await new Promise<void>(finish => { exitWait.current = { finish, timer: setTimeout(finish, 200) } })
      exitWait.current = null
    }
    if (!mounted.current) return
    try {
      await (openPanel ? gateway.openPanel() : gateway.close())
      if (mounted.current) setClosed(true)
    } catch {
      if (mounted.current) {
        leaving.current = false; setPhase('open')
        setError(openPanel ? '无法打开面板，请重试' : '无法关闭提示，请重试')
      }
    }
  }
  function accept(next: PromptState) {
    if (!mounted.current || leaving.current) return
    const arrival = next.pending.find(p => p.Token === selection.current.token) ?? next.pending[0]
    // Keep the last card intact while it exits instead of flashing an empty state.
    if (!arrival) { void close(); return }
    if (!firstToken.current) firstToken.current = arrival.Token
    if (arrival.Token !== selection.current.token) { setChoosing(false); setHoverAction(null); setFocusAction(null) }
    setState(next)
    const devices = next.snapshot.Devices.filter(d => d.Online && d.Flow === arrival.Flow)
    const retained = arrival.Token === selection.current.token && devices.some(d => d.Id === selection.current.id)
    const id = retained ? selection.current.id : devices.find(d => arrival.NewIds.includes(d.Id))?.Id ?? next.snapshot.Defaults[`${arrival.Flow}:1`] ?? devices[0]?.Id ?? ''
    selection.current = { token: arrival.Token, id }; setToken(arrival.Token); setSelected(id)
  }
  async function refresh() {
    if (lock.current || leaving.current) return
    lock.current = true; setBusy(true)
    try { const next = await gateway.read(); paused.current = false; setError(''); accept(next) }
    catch (e) { paused.current = true; setError(e instanceof Error ? e.message : String(e)) }
    finally { lock.current = false; setBusy(false) }
  }
  useEffect(() => {
    document.title = gateway.mode === 'desktop' ? '声间 设备提示' : '声间 设备提示预览'
    document.querySelector('meta[name="description"]')?.setAttribute('content', gateway.mode === 'desktop'
      ? '声间 Audio Switch 音频设备接入与断开提示'
      : '声间 Audio Switch 仅使用模拟数据的设备提示预览')
  }, [gateway.mode])
  useEffect(() => {
    if (closed) return
    let active = true, timer: ReturnType<typeof setTimeout>
    async function tick() {
      if (!active) return
      // Reads and writes share a lock; never let a late read overwrite a response.
      if (!lock.current && !paused.current) {
        lock.current = true
        try { const next = await gateway.read(); if (active && !leaving.current) accept(next) }
        catch (e) { if (active && !leaving.current) { paused.current = true; setError(e instanceof Error ? e.message : String(e)) } }
        finally { lock.current = false }
      }
      if (active) timer = setTimeout(tick, 300)
    }
    void tick()
    return () => { active = false; clearTimeout(timer) }
    // The gateway is fixed for the lifetime of this window.
  }, [gateway, closed])
  useEffect(() => { if (closed) paused.current = true }, [closed])
  useEffect(() => {
    const dark = state?.snapshot.Preferences.DarkMode ?? true
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
  }, [state?.snapshot.Preferences.DarkMode])
  const loaded = !!state || !!error, dark = state?.snapshot.Preferences.DarkMode ?? true
  useEffect(() => {
    if (!loaded || leaving.current) return
    let active = true, frame = 0
    // The themed first frame is ready before the native window becomes visible.
    void (gateway.ready?.(dark) ?? Promise.resolve()).catch(() => {}).then(() => {
      if (active && !leaving.current) frame = requestAnimationFrame(() => {
        if (active && !leaving.current) setPhase('open')
      })
    })
    return () => { active = false; cancelAnimationFrame(frame) }
  }, [gateway, loaded, dark])
  async function respond(action: PromptAction) {
    if (lock.current || paused.current || leaving.current) return
    lock.current = true; setBusy(true); setError('')
    try { accept(await gateway.respond(action)) }
    catch (e) { paused.current = true; if (e instanceof PromptFailure && e.state) accept(e.state); setError(e instanceof Error ? e.message : String(e)) }
    finally { lock.current = false; setBusy(false) }
  }
  const arrival = state?.pending.find(p => p.Token === token)
  const devices = state?.snapshot.Devices.filter(d => d.Online && d.Flow === arrival?.Flow) ?? []
  if (state?.snapshot.Preferences.UseDevicePriority) {
    const order = state.snapshot.Preferences.DeviceOrder[arrival?.Flow ?? 0]
    devices.sort((a, b) => order.indexOf(a.Id) - order.indexOf(b.Id))
  }
  const currentId = arrival && state?.snapshot.Defaults[`${arrival.Flow}:1`]
  const eventDevices = state?.snapshot.Devices.filter(d => arrival?.NewIds.includes(d.Id)) ?? []
  const eventName = arrival?.Disconnected ? arrival.PreviousName : eventDevices.map(d => d.Name).join('、')
  const currentName = devices.find(d => d.Id === currentId)?.Name ?? '没有可用设备'
  const canSelect = !!state?.snapshot.CanWrite && !error && devices.some(d => d.Id === selected)
  const canPrevious = !!state && !!arrival && !error && state.snapshot.CanWrite && canRestore(state, arrival)
  const action = hoverAction ?? focusAction
  const feedback = action === 'close' ? 'close' : action === 'selected' && canSelect ? 'selected' : action === 'previous' && canPrevious ? 'previous' : 'current'
  const targetName = devices.find(d => d.Id === selected)?.Name ?? '没有可用设备'
  return <main className={`prompt-shell ${dark ? 'dark' : ''}`} data-phase={phase} data-choosing={choosing} data-closed={closed || undefined} onKeyDown={event => {
    if (event.key === 'Escape' && !event.defaultPrevented && !busy) { event.preventDefault(); void close() }
  }}>
    <header className="prompt-header">
      <span className="prompt-brand"><AudioLines size={16} aria-hidden="true" />声间</span>
      {state && state.pending.length > 1 && <span className="prompt-count" aria-label={`${state.pending.length} 个待处理提示`}>{state.pending.length} 个提示</span>}
      <div className="prompt-tools">
        <Button variant="ghost" size="icon" aria-label="打开面板" title="打开面板" pending={busy || phase === 'closing'} onClick={() => void close(true)}><ArrowUpRight size={16} /></Button>
        <Button variant="ghost" size="icon" aria-label="关闭提示" title="关闭提示" pending={busy || phase === 'closing'} onMouseEnter={() => setHoverAction('close')} onMouseLeave={() => setHoverAction(null)} onFocus={() => setFocusAction('close')} onBlur={() => setFocusAction(null)} onClick={() => void close()}><X size={16} /></Button>
      </div>
    </header>
    {gateway.mode === 'preview' && <div className="prompt-preview">界面预览，操作不会改变系统设置</div>}
    <ScrollSurface className="prompt-content">
      {closed ? <p role="status" className="prompt-closed"><Check size={20} />提示已关闭</p> : <section className="prompt-event" key={token} data-advance={token !== firstToken.current || undefined} aria-busy={busy}>
        <div className="prompt-hero">
          <span className="prompt-device-art" data-disconnected={arrival?.Disconnected || undefined}>
            {arrival?.Disconnected ? <Unplug size={24} /> : eventDevices[0] ? <DeviceIcon device={eventDevices[0]} size={24} /> : arrival?.Flow === 1 ? <Mic size={24} /> : <Headphones size={24} />}
          </span>
          <div className="prompt-heading">
            <h1>{arrival ? arrival.Disconnected ? '当前设备已断开' : `发现新的${arrival.Flow === 0 ? '输出设备' : '麦克风'}` : error ? '无法读取设备提示' : '正在读取设备提示'}</h1>
            {arrival && <p className="prompt-device-name" title={eventName}>{eventName || (arrival.Flow === 0 ? '声音输出' : '麦克风输入')}</p>}
          </div>
        </div>
        {arrival && state && <>
          <div className="prompt-outcome" aria-live="polite" aria-atomic="true" data-preview={feedback !== 'current' || undefined}>
            {[
              { key: 'current', label: '当前使用', name: currentName },
              { key: 'close', label: '将保持', name: currentName },
              { key: 'selected', label: '将使用', name: targetName },
              { key: 'previous', label: '将恢复', name: arrival.PreviousName },
            ].map(item => <div key={item.key} className="prompt-current" data-active={feedback === item.key} aria-hidden={feedback !== item.key}>
              <span>{item.label}</span><span title={item.name}>{item.name}</span>
            </div>)}
          </div>
          <Button className="prompt-choose" variant="ghost" size="sm" aria-expanded={choosing} aria-controls="prompt-device-choice" disabled={!!error || !state.snapshot.CanWrite || !devices.length} pending={busy || phase === 'closing'} onClick={() => setChoosing(value => !value)}>选择其他设备<ChevronDown size={14} aria-hidden="true" /></Button>
          <div id="prompt-device-choice" className="prompt-choice" data-open={choosing} aria-hidden={!choosing} inert={!choosing}>
          <div className="prompt-choice-inner">
          <Select id="prompt-device" label={arrival.Flow === 0 ? '声音输出' : '麦克风输入'} value={devices.length ? selected : ''} disabled={!!error || !state.snapshot.CanWrite || !devices.length} pending={busy || phase === 'closing'} onValueChange={value => { selection.current.id = value; setSelected(value) }} options={devices.length ? devices.map((d, i) => ({ value: d.Id, label: `${d.Name}${devices.some(other => other.Id !== d.Id && other.Name === d.Name) ? `（${i + 1}）` : ''}` })) : [{ value: '', label: '没有可用设备' }]} />
          </div></div>
          {!state.snapshot.CanWrite && <p role="status" className="prompt-readonly">当前后台仅支持查看，请更新后台后再操作</p>}
        </>}
        {error && <div role="alert" className="prompt-error"><p>{error}</p><Button variant="outline" disabled={busy || phase === 'closing'} onClick={() => void refresh()}>刷新状态</Button></div>}
      </section>}
    </ScrollSurface>
    {!closed && arrival && state && <footer className="prompt-actions" aria-busy={busy}>
      <Button className="prompt-primary" disabled={!canSelect} pending={busy || phase === 'closing'} onMouseEnter={() => setHoverAction('selected')} onMouseLeave={() => setHoverAction(null)} onFocus={() => setFocusAction('selected')} onBlur={() => setFocusAction(null)} onClick={() => void respond({ kind: 'selected', token, id: selected })}>{busy && <LoaderCircle className="spin" aria-hidden="true" />}{!arrival.Disconnected && arrival.NewIds.includes(selected) ? '使用新设备' : '切换'}</Button>
      <div className="prompt-secondary">
        {!arrival.Disconnected && <Button variant="ghost" disabled={!canPrevious} pending={busy || phase === 'closing'} onMouseEnter={() => setHoverAction('previous')} onMouseLeave={() => setHoverAction(null)} onFocus={() => setFocusAction('previous')} onBlur={() => setFocusAction(null)} onClick={() => void respond({ kind: 'previous', token })}>保留原设备</Button>}
      </div>
    </footer>}
  </main>
}
