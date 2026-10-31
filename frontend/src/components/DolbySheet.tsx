import { useEffect, useRef, useState } from 'react'
import type { Device, Snapshot, UiGateway } from '@/data/types'
import { dolbyProfile, emptyDolby, frequencies, toTen, toTwenty, type DolbyProfile, type DolbyRead } from '@/data/dolby'
import { OperationFailure } from '@/data/desktop'
import { useDolbyApply } from './useDolbyApply'
import { Button } from './ui/button'
import { Switch } from './ui/switch'
import { Select } from './ui/select'
import { Sheet, SheetContent, SheetTitle, SheetDescription } from './ui/sheet'
import { Crossfade } from './Crossfade'

function NumberField({ value, onValue, min, max, step, label, disabled = false }: { value: number | null; onValue: (v: number) => void; min: number; max: number; step: number; label: string; disabled?: boolean }) {
  const [text, setText] = useState(value === null ? '' : String(value))
  useEffect(() => { setText(value === null ? '' : String(value)) }, [value])
  return <input aria-label={label} type="number" min={min} max={max} step={step} disabled={disabled} placeholder={value === null ? '保持不变' : undefined} value={text} onChange={e => {
    setText(e.target.value); const n = e.target.valueAsNumber
    if (Number.isFinite(n) && n >= min && n <= max && (step !== 1 || Number.isInteger(n))) onValue(n)
  }} onBlur={() => setText(value === null ? '' : String(value))} />
}
function EqSlider({ label, value, onValue }: { label: string; value: number; onValue: (value: number) => void }) {
  const pointer = useRef<{ x: number; moved: boolean } | null>(null)
  return <input aria-label={label} type="range" min="-12" max="12" step="0.1" value={value}
    onPointerDown={e => { pointer.current = { x:e.clientX, moved:false } }}
    onPointerMove={e => { if (pointer.current && Math.abs(e.clientX-pointer.current.x) >= 1) pointer.current.moved = true }}
    onPointerUp={() => { pointer.current = null }} onPointerCancel={() => { pointer.current = null }}
    onChange={e => { if (!pointer.current || pointer.current.moved) onValue(e.target.valueAsNumber) }} />
}

function StrengthValue({ label, value, disabled, onValue }: { label: string; value: number | null; disabled: boolean; onValue: (value: number) => void }) {
  const last = useRef(value ?? .5)
  if (value !== null) last.current = value
  const visible = value !== null
  return <div className="dolby-strength-value" data-visible={visible} aria-hidden={!visible || undefined} inert={!visible}>
    <NumberField label={`${label}百分比`} min={0} max={100} step={0.01} disabled={disabled || !visible} value={Number((last.current * 100).toFixed(8))} onValue={onValue} /><span>%</span>
  </div>
}

export function DolbyFields({ value: p, onChange, disabled = false }: { value: DolbyProfile; onChange: (p: DolbyProfile) => void; disabled?: boolean }) {
  const [advanced, setAdvanced] = useState(false), rememberedEq = useRef(p.Eq ?? Array<number>(20).fill(0))
  if (p.Eq) rememberedEq.current = p.Eq
  const raw = p.Eq ?? rememberedEq.current, ten = toTen(raw), custom = p.MainProfile === 4
  const change = <K extends keyof DolbyProfile>(key: K, value: DolbyProfile[K]) => onChange({ ...p, [key]:value })
  function editTen(index: number, value: number) {
    if (advanced || !Number.isFinite(value) || value < -12 || value > 12 || value === ten[index]) return
    const next = [...ten]; next[index] = value; change('Eq', toTwenty(next))
  }
  return <div className="dolby-fields">
    <div className="dolby-preset-label"><label htmlFor="dolby-preset">Dolby 预设</label>{custom && <span className="role-badge">设备共享</span>}</div><Select disabled={disabled} id="dolby-preset" label="Dolby 预设" value={p.MainProfile === 0 ? 'movie' : custom ? String(p.SubProfile) : 'keep'} onValueChange={preset => {
      onChange({ ...p, MainProfile:preset === 'movie' ? 0 : preset === 'keep' ? null : 4, SubProfile:preset === '4' ? 4 : preset === '5' ? 5 : null, Ieq:preset === 'movie' ? p.Ieq : null, Eq:preset === '4' || preset === '5' ? p.Eq : null })
    }} options={[{ value: 'keep', label: '保持当前预设' }, { value: 'movie', label: '电影' }, { value: '4', label: '自定义 1' }, { value: '5', label: '自定义 2' }]} />
    <div className="dolby-grid">{([['Enabled','Dolby 总开关'],['Surround','环绕虚拟化'],['Dialog','人声增强'],['Leveler','音量平衡'],['AutoSwitch','内容自动切换']] as const).map(([key,label]) => <div className="dolby-field" key={key}><label htmlFor={`dolby-${key}`}>{label}</label><Select disabled={disabled} id={`dolby-${key}`} label={label} value={p[key] === null ? 'keep' : p[key] ? 'on' : 'off'} onValueChange={value => change(key, value === 'keep' ? null : value === 'on')} options={[{ value: 'keep', label: '保持不变' }, { value: 'on', label: '开启' }, { value: 'off', label: '关闭' }]} /></div>)}</div>
    <div className="dolby-strengths">{([['SurroundStrength','环绕强度'],['DialogStrength','人声强度'],['LevelerStrength','音量平衡强度']] as const).map(([key,label]) => <div key={key} className="dolby-strength"><label htmlFor={`dolby-${key}`}>{label}</label><StrengthValue label={label} value={p[key]} disabled={disabled} onValue={n => change(key, n / 100)} /><Switch disabled={disabled} id={`dolby-${key}`} checked={p[key] !== null} onCheckedChange={checked => change(key, checked ? 0.5 : null)} /></div>)}</div>
    <Crossfade value={[p.SurroundStrength, p.DialogStrength, p.LevelerStrength].some(value => value !== null) ? 'shown' : 'hidden'} panes={[{ key: 'shown', render: () => <p className="field-help">强度可能按驱动档位取整。</p> }, { key: 'hidden', render: () => null }]} />
    <label htmlFor="dolby-ieq">智能均衡器（电影）</label><Select id="dolby-ieq" label="智能均衡器（电影）" disabled={disabled || p.MainProfile !== 0} value={String(p.Ieq ?? 'keep')} onValueChange={value => change('Ieq', value === 'keep' ? null : Number(value) as 0 | 2)} options={[{ value: 'keep', label: '保持不变' }, { value: '0', label: '关闭' }, { value: '2', label: '平衡' }]} />
    <section className="sheet-section"><div className="volume-toggle"><label htmlFor="dolby-eq-enabled">均衡器（自定义预设）</label><Switch id="dolby-eq-enabled" disabled={disabled || !custom} checked={p.Eq !== null} onCheckedChange={checked => change('Eq', checked ? [...rememberedEq.current] : null)} /></div>
      <div className="dolby-eq-toolbar"><div className="segmented dolby-views" aria-label="均衡器视图"><Button type="button" variant="ghost" aria-pressed={!advanced} onClick={() => setAdvanced(false)}>10 点 dB</Button><Button type="button" variant="ghost" aria-pressed={advanced} onClick={() => setAdvanced(true)}>20 段原始值</Button></div><span className="dolby-eq-range">{advanced ? '−192～192 驱动单位' : '−12～12 dB'}</span></div>
      <Crossfade value={advanced ? 'raw' : 'curve'} panes={[
        { key: 'curve', render: () => <fieldset className="dolby-eq dolby-eq-curve" disabled={disabled || !custom || p.Eq === null}><legend className="sr-only">均衡器数值</legend>
          {ten.map((v,i) => <label key={i}><span>{frequencies[i]}</span><NumberField label={`${frequencies[i]} dB`} min={-12} max={12} step={0.01} value={v} onValue={n => editTen(i,n)} /><EqSlider label={`${frequencies[i]} dB 滑块`} value={v} onValue={n => editTen(i,n)} /></label>)}
        </fieldset> },
        { key: 'raw', render: () => <fieldset className="dolby-eq" disabled={disabled || !custom || p.Eq === null}><legend className="sr-only">均衡器数值</legend>
          {raw.map((v,i) => <label key={i}>第 {i+1} 段<NumberField label={`第 ${i+1} 段原始值`} min={-192} max={192} step={1} value={v} onValue={n => { if (!advanced) return; const next = [...raw]; next[i] = n; change('Eq', next) }} /></label>)}
        </fieldset> },
      ]} />
    </section>
  </div>
}

export function DolbySheet({ device, snapshot, gateway, readOnly, onClose: finishClose, onSaved, onFailure, onResult }: {
  device: Device; snapshot: Snapshot; gateway: UiGateway; readOnly: boolean; onClose: () => void
  onSaved: (snapshot: Snapshot) => void; onFailure: (error: OperationFailure) => void
  onResult?: (text: string, error: boolean) => void
}) {
  const [open, setOpen] = useState(true)
  const onClose = () => setOpen(false)

  const desktop = gateway.mode === 'desktop', supported = !desktop || !!snapshot.CanEditDolby
  const [baseline, setBaseline] = useState(() => structuredClone(snapshot.DolbyProfiles?.[device.Id] ?? null))
  const [enabled, setEnabled] = useState(baseline !== null), [draft, setDraft] = useState<DolbyProfile>(() => baseline ?? emptyDolby())
  const [job, setJob] = useState<DolbyRead | null>(null), [starting, setStarting] = useState(false), [saving, setSaving] = useState(false), [closing, setClosing] = useState(false)
  const [error, setError] = useState(''), [message, setMessage] = useState(''), [uncertain, setUncertain] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null), current = useRef<DolbyRead | null>(null), alive = useRef(true), lock = useRef(false), saveLock = useRef(false), closeRequested = useRef(false), cancelling = useRef(false)
  const application = useDolbyApply(gateway, device.Id, next => { setBaseline(structuredClone(next.DolbyProfiles?.[device.Id] ?? null)); onSaved(next) }, onClose, onFailure, onSaved, onResult)
  const running = job?.Status === 'running', blocked = saving || starting || running || closing || readOnly || !supported || uncertain || application.busy || application.uncertain
  function accept(value: DolbyRead, fill = true) {
    current.current = value
    if (!alive.current) return
    setJob(value); setMessage(!fill && value.Status === 'ready' ? '读取已经结束；本次未替换编辑草稿。' : value.Message)
    if (fill && !cancelling.current && !closeRequested.current && value.Status === 'ready' && value.Profile) { setDraft(value.Profile); setEnabled(true) }
  }
  useEffect(() => { alive.current = true; return () => { alive.current = false; if (current.current?.Status === 'running') void gateway.cancelDolbyRead(current.current.Token).catch(() => {}) } }, [gateway])
  useEffect(() => {
    if (!job || !running || closing || error) return
    let active = true
    const timer = setTimeout(() => { gateway.readDolby(job.Token).then(value => { if (active) accept(value) }).catch(e => { if (active) setError(e instanceof Error ? e.message : '读取失败，原草稿已保留。') }) }, 250)
    return () => { active = false; clearTimeout(timer) }
  }, [job, running, closing, error, gateway])
  async function stop(close: boolean) {
    if (application.busy) { await application.cancel(close); return }
    if (saveLock.current) return
    cancelling.current = true
    if (lock.current) { if (close) { closeRequested.current = true; setClosing(true) }; return }
    lock.current = true; setClosing(true); setError('')
    try { if (current.current?.Status === 'running') accept(await gateway.cancelDolbyRead(current.current.Token), false); if (close && alive.current) onClose() }
    catch (e) { if (alive.current) setError(e instanceof Error ? e.message : '尚未确认读取停止，请重试。') }
    finally { lock.current = false; closeRequested.current = false; cancelling.current = false; if (alive.current) setClosing(false) }
  }
  async function start() {
    if (lock.current || current.current?.Status === 'running') return
    lock.current = true; setStarting(true); setError(''); setMessage('')
    try {
      const value = await gateway.startDolbyRead(device.Id); accept(value)
      if (!alive.current || closeRequested.current || cancelling.current) {
        if (value.Status === 'running') accept(await gateway.cancelDolbyRead(value.Token), false)
        if (alive.current && closeRequested.current) onClose()
      }
    } catch (e) { if (alive.current) setError(e instanceof Error ? e.message : '读取失败，原草稿已保留。') }
    finally { lock.current = false; closeRequested.current = false; cancelling.current = false; if (alive.current) { setStarting(false); setClosing(false) } }
  }
  async function save() {
    if (lock.current || blocked) return
    lock.current = saveLock.current = true; setSaving(true); setError('')
    try { const next = await gateway.saveDolby(device.Id, enabled ? dolbyProfile(draft) : null, baseline); onSaved(next); onClose() }
    catch (e) {
      setError(e instanceof Error ? e.message : '保存失败，草稿已保留。')
      if (e instanceof OperationFailure) { if (e.requiresRefresh) setUncertain(true); onFailure(e) }
    } finally { lock.current = saveLock.current = false; setSaving(false) }
  }
  return <Sheet open={open} onOpenChangeComplete={value => { if (!value) finishClose() }} onOpenChange={open => { if (!open) void stop(true) }}><SheetContent nested className="dolby-sheet" initialFocus={heading} closeDisabled={saving || closing} closeLabel="关闭 Dolby 编辑器">
    <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">Dolby 方案</SheetTitle><SheetDescription className="sheet-description">{device.Name}{!device.Online ? '（离线）' : ''}</SheetDescription></header>
    {!desktop && <div className="sheet-preview">界面预览，操作不会改变系统设置</div>}
    <div className="sheet-scroll">
      {!supported && <p role="status">当前后台暂不支持新 Dolby 编辑器，请使用新版后台。</p>}
      <Button variant="outline" disabled={blocked || !device.Online || snapshot.Defaults['0:1'] !== device.Id || !!snapshot.DolbyApplying} onClick={() => void start()}>{desktop ? '读取当前 Dolby 并填入' : '模拟读取 Dolby 并填入'}</Button>
      {snapshot.Defaults['0:1'] !== device.Id && <p className="field-help">切换到此设备后才能读取或立即应用。</p>}
      {(starting || running || closing) && <p role="status">{closing ? '正在取消并等待读取退出…' : '正在读取 Dolby…'}</p>}
      {message && <p role={job?.Status === 'error' ? 'alert' : 'status'}>{message}</p>}
      {error && <p role="alert" className="field-help">{error}</p>}
      {running && error && <Button variant="outline" disabled={closing} onClick={() => setError('')}>重新读取状态</Button>}
      <div className="volume-toggle"><label htmlFor="use-dolby">成为输出时自动应用此方案</label><Switch id="use-dolby" checked={enabled} disabled={blocked} onCheckedChange={setEnabled} /></div>
      <fieldset className="dolby-controls" disabled={blocked || !enabled}><legend className="sr-only">Dolby 参数</legend><DolbyFields value={draft} onChange={setDraft} disabled={blocked || !enabled} /></fieldset>
    </div>
    <footer className="sheet-footer">
      {application.job && <p role={['error','warning','cancelled'].includes(application.job.Status) ? 'alert' : 'status'}>{application.job.Message}</p>}
      {application.error && <p role="alert">{application.error}</p>}
      <div><Button variant="outline" disabled={saving || closing} onClick={() => void stop(true)}>{application.job ? '关闭' : '取消'}</Button>
        {running && <Button variant="outline" disabled={closing} onClick={() => void stop(false)}>取消读取</Button>}
        {application.busy && <Button variant="outline" onClick={() => void application.cancel()}>取消应用</Button>}
        {application.error && application.busy && <Button variant="outline" onClick={application.retry}>检查本次应用</Button>}
        <Button variant="outline" disabled={blocked} onClick={() => void save()}>{saving ? '正在保存…' : desktop ? '仅保存' : '仅保存（模拟）'}</Button>
        <Button disabled={blocked || !enabled || !device.Online || snapshot.Defaults['0:1'] !== device.Id || !!snapshot.DolbyApplying || (desktop && !snapshot.CanApplyDolby)} onClick={() => { const p = dolbyProfile(draft); if (p) void application.start(p, baseline) }}>{desktop ? '保存并应用' : '保存并应用（模拟）'}</Button>
      </div>
      {desktop && !snapshot.CanApplyDolby && <p>当前后台不支持此处立即应用，可继续仅保存。</p>}
    </footer>
  </SheetContent></Sheet>
}
