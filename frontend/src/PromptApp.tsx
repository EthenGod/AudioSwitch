import { useEffect, useRef, useState } from 'react'
import { Headphones, Mic, X } from 'lucide-react'
import { Button } from './components/ui/button'
import { canRestore, PromptFailure, type PromptAction, type PromptGateway, type PromptState } from './data/prompt'

export function PromptApp({ gateway }: { gateway: PromptGateway }) {
  const [state, setState] = useState<PromptState>(), [token, setToken] = useState(''), [selected, setSelected] = useState('')
  const [error, setError] = useState(''), [busy, setBusy] = useState(false), [closed, setClosed] = useState(false)
  const lock = useRef(false), paused = useRef(false), current = useRef<PromptState | undefined>(undefined)
  const selection = useRef({ token: '', id: '' })
  async function close() {
    try { await gateway.close(); setClosed(true) } catch { paused.current = true; setError('无法关闭窗口，请用窗口关闭按钮重试。') }
  }
  function accept(next: PromptState) {
    current.current = next; setState(next)
    const arrival = next.pending.find(p => p.Token === selection.current.token) ?? next.pending[0]
    if (!arrival) { void close(); return }
    const devices = next.snapshot.Devices.filter(d => d.Online && d.Flow === arrival.Flow)
    const retained = arrival.Token === selection.current.token && devices.some(d => d.Id === selection.current.id)
    const id = retained ? selection.current.id : devices.find(d => arrival.NewIds.includes(d.Id))?.Id ?? next.snapshot.Defaults[`${arrival.Flow}:1`] ?? devices[0]?.Id ?? ''
    selection.current = { token: arrival.Token, id }; setToken(arrival.Token); setSelected(id)
  }
  async function refresh() {
    if (lock.current) return
    lock.current = true; setBusy(true)
    try { const next = await gateway.read(); paused.current = false; setError(''); accept(next) }
    catch (e) { paused.current = true; setError(e instanceof Error ? e.message : String(e)) }
    finally { lock.current = false; setBusy(false) }
  }
  useEffect(() => {
    document.title = gateway.mode === 'desktop' ? '声间 · 设备提示' : '声间 · 设备提示预览'
  }, [gateway.mode])
  useEffect(() => {
    let active = true, timer: ReturnType<typeof setTimeout>
    async function tick() {
      if (!active) return
      // Reads and writes share a lock; never let a late read overwrite a response.
      if (!lock.current && !paused.current) {
        lock.current = true
        try { const next = await gateway.read(); if (active) accept(next) }
        catch (e) { if (active) { paused.current = true; setError(e instanceof Error ? e.message : String(e)) } }
        finally { lock.current = false }
      }
      if (active) timer = setTimeout(tick, current.current?.snapshot.Preferences.GameMode ? 1500 : 300)
    }
    void tick()
    return () => { active = false; clearTimeout(timer) }
    // The gateway is fixed for the lifetime of this window.
  }, [gateway])
  useEffect(() => { if (closed) paused.current = true }, [closed])
  useEffect(() => {
    const dark = state?.snapshot.Preferences.DarkMode ?? true
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
  }, [state?.snapshot.Preferences.DarkMode])
  async function respond(action: PromptAction) {
    if (lock.current || paused.current) return
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
  const disabled = busy || !!error || !state?.snapshot.CanWrite
  const currentId = arrival && state?.snapshot.Defaults[`${arrival.Flow}:1`]
  return <main className={`prompt-shell ${state?.snapshot.Preferences.DarkMode ? 'dark' : ''}`}>
    <header className="prompt-header"><span>{arrival?.Flow === 1 ? <Mic size={18} /> : <Headphones size={18} />} 声间</span><Button variant="ghost" size="icon" aria-label="关闭提示" disabled={busy} onClick={() => void close()}><X size={16} /></Button></header>
    {gateway.mode === 'preview' && <div className="prompt-preview">界面预览，操作不会改变系统设置</div>}
    <section className="prompt-content" aria-busy={busy}>
      {closed ? <p role="status">提示已关闭</p> : <>
        <h1>{arrival ? arrival.Disconnected ? '当前设备已断开' : `发现新的${arrival.Flow === 0 ? '输出设备' : '麦克风'}` : error ? '无法读取设备提示' : '正在读取设备提示'}</h1>
        {arrival && state && <>
          <p className="muted">{arrival.Disconnected ? arrival.PreviousName : state.snapshot.Devices.filter(d => arrival.NewIds.includes(d.Id)).map(d => d.Name).join('、')}</p>
          <p>当前：{devices.find(d => d.Id === currentId)?.Name ?? '没有可用设备'}</p>
          {state.pending.length > 1 && <p className="muted">还有 {state.pending.length - 1} 个提示待处理</p>}
          <label htmlFor="prompt-device">{arrival.Flow === 0 ? '声音输出' : '麦克风输入'}</label>
          <select id="prompt-device" value={selected} disabled={disabled || !devices.length} onChange={e => { selection.current.id = e.target.value; setSelected(e.target.value) }}>
            {!devices.length && <option value="">没有可用设备</option>}
            {devices.map((d, i) => <option key={d.Id} value={d.Id}>{d.Name}{devices.some(other => other.Id !== d.Id && other.Name === d.Name) ? `（${i + 1}）` : ''}</option>)}
          </select>
          <p className="muted">{state.snapshot.Preferences.IncludeCommunications ? '同时切换通话设备' : '保留通话设备选择'}</p>
          {!state.snapshot.CanWrite && <p role="status">当前后台仅支持查看，请更新后台后再操作。</p>}
        </>}
        {error && <div role="alert" className="prompt-error">{error}<Button variant="outline" disabled={busy} onClick={() => void refresh()}>刷新状态</Button></div>}
      </>}
    </section>
    {!closed && <footer className="prompt-actions">
      {arrival && state && <>
        <Button disabled={disabled || !devices.some(d => d.Id === selected)} onClick={() => void respond({ kind: 'selected', token, id: selected })}>{busy ? '正在处理…' : !arrival.Disconnected && arrival.NewIds.includes(selected) ? '用新设备' : '切换'}</Button>
        {!arrival.Disconnected && <Button variant="outline" title={arrival.PreviousName} disabled={disabled || !canRestore(state, arrival)} onClick={() => void respond({ kind: 'previous', token })}>继续用旧设备</Button>}
        <Button variant="outline" disabled={disabled} onClick={() => void respond({ kind: 'current', token })}>{devices.length ? '保持当前选择' : '知道了'}</Button>
      </>}
      <Button variant="ghost" disabled={busy} onClick={() => { if (!lock.current) { lock.current = true; setBusy(true); void gateway.openPanel().then(() => setClosed(true)).catch(() => setError('无法打开面板，请重试。')).finally(() => { lock.current = false; setBusy(false) }) } }}>打开面板</Button>
    </footer>}
  </main>
}
