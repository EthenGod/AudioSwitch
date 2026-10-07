import { useEffect, useRef, useState } from 'react'
import type { Device, Snapshot, UiGateway } from '@/data/types'
import { dolbyProfile, emptyDolby, frequencies, toTen, toTwenty, type DolbyProfile, type DolbyRead } from '@/data/dolby'
import { OperationFailure } from '@/data/desktop'
import { Button } from './ui/button'
import { Switch } from './ui/switch'
import { Sheet, SheetContent, SheetTitle, SheetDescription } from './ui/sheet'

function NumberField({ value, onValue, min, max, step, label, disabled = false }: { value: number | null; onValue: (v: number) => void; min: number; max: number; step: number; label: string; disabled?: boolean }) {
  const [text, setText] = useState(value === null ? '' : String(value))
  useEffect(() => { setText(value === null ? '' : String(value)) }, [value])
  return <input aria-label={label} type="number" min={min} max={max} step={step} disabled={disabled} value={text} onChange={e => {
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

export function DolbyFields({ value: p, onChange }: { value: DolbyProfile; onChange: (p: DolbyProfile) => void }) {
  const [advanced, setAdvanced] = useState(false), rememberedEq = useRef(p.Eq ?? Array<number>(20).fill(0))
  if (p.Eq) rememberedEq.current = p.Eq
  const raw = p.Eq ?? rememberedEq.current, ten = toTen(raw), custom = p.MainProfile === 4
  const change = <K extends keyof DolbyProfile>(key: K, value: DolbyProfile[K]) => onChange({ ...p, [key]:value })
  function editTen(index: number, value: number) {
    if (!Number.isFinite(value) || value < -12 || value > 12 || value === ten[index]) return
    const next = [...ten]; next[index] = value; change('Eq', toTwenty(next))
  }
  return <div className="dolby-fields">
    <label htmlFor="dolby-preset">Dolby 预设</label><select id="dolby-preset" value={p.MainProfile === 0 ? 'movie' : custom ? String(p.SubProfile) : 'keep'} onChange={e => {
      const preset = e.target.value
      onChange({ ...p, MainProfile:preset === 'movie' ? 0 : preset === 'keep' ? null : 4, SubProfile:preset === '4' ? 4 : preset === '5' ? 5 : null, Ieq:preset === 'movie' ? p.Ieq : null, Eq:preset === '4' || preset === '5' ? p.Eq : null })
    }}><option value="keep">保持当前预设</option><option value="movie">电影</option><option value="4">自定义 1</option><option value="5">自定义 2</option></select>
    <div className="dolby-grid">{([['Enabled','Dolby 总开关'],['Surround','环绕虚拟化'],['Dialog','人声增强'],['Leveler','音量平衡'],['AutoSwitch','内容自动切换']] as const).map(([key,label]) => <label key={key}>{label}<select aria-label={label} value={p[key] === null ? 'keep' : p[key] ? 'on' : 'off'} onChange={e => change(key, e.target.value === 'keep' ? null : e.target.value === 'on')}><option value="keep">保持不变</option><option value="on">开启</option><option value="off">关闭</option></select></label>)}</div>
    <div className="dolby-strengths">{([['SurroundStrength','环绕强度'],['DialogStrength','人声强度'],['LevelerStrength','音量平衡强度']] as const).map(([key,label]) => <div key={key} className="dolby-strength"><label><input type="checkbox" checked={p[key] !== null} onChange={e => change(key, e.target.checked ? 0.5 : null)} />{label}</label><NumberField label={`${label}百分比`} min={0} max={100} step={0.01} disabled={p[key] === null} value={p[key] === null ? null : Number((p[key] * 100).toFixed(8))} onValue={n => change(key, n / 100)} /><span>%</span></div>)}</div>
    <p className="field-help">未勾选的强度保持原值；驱动可能按档位取整。</p>
    <label htmlFor="dolby-ieq">智能均衡器（电影）</label><select id="dolby-ieq" disabled={p.MainProfile !== 0} value={p.Ieq ?? 'keep'} onChange={e => change('Ieq', e.target.value === 'keep' ? null : Number(e.target.value) as 0 | 2)}><option value="keep">保持不变</option><option value="0">关闭</option><option value="2">平衡</option></select>
    <section className="sheet-section"><label className="dolby-eq-toggle"><input type="checkbox" disabled={!custom} checked={p.Eq !== null} onChange={e => change('Eq', e.target.checked ? [...rememberedEq.current] : null)} />应用均衡器（自定义预设）</label>
      <div className="segmented dolby-views" aria-label="均衡器视图"><Button type="button" variant="ghost" aria-pressed={!advanced} onClick={() => setAdvanced(false)}>10 点 dB</Button><Button type="button" variant="ghost" aria-pressed={advanced} onClick={() => setAdvanced(true)}>20 段原始值</Button></div>
      <p className="field-help">{advanced ? '原始驱动单位，范围 −192～192，不是 dB。' : '范围 −12～12 dB。修改任意一点会重新换算 20 段曲线。'}切换视图不会修改曲线。</p>
      <fieldset className="dolby-eq" disabled={!custom || p.Eq === null}><legend className="sr-only">均衡器数值</legend>
        {advanced ? raw.map((v,i) => <label key={i}>第 {i+1} 段<NumberField label={`第 ${i+1} 段原始值`} min={-192} max={192} step={1} value={v} onValue={n => { const next = [...raw]; next[i] = n; change('Eq', next) }} /></label>) : ten.map((v,i) => <label key={i}>{frequencies[i]}<EqSlider label={`${frequencies[i]} dB 滑块`} value={v} onValue={n => editTen(i,n)} /><NumberField label={`${frequencies[i]} dB`} min={-12} max={12} step={0.01} value={v} onValue={n => editTen(i,n)} /></label>)}
      </fieldset>
    </section>
    <p className="field-help">Dolby 自定义槽位由驱动共享。内容自动切换可能改变当前预设。低音增强暂不支持。</p>
  </div>
}

export function DolbySheet({ device, snapshot, gateway, readOnly, onClose, onSaved, onFailure }: {
  device: Device; snapshot: Snapshot; gateway: UiGateway; readOnly: boolean; onClose: () => void
  onSaved: (snapshot: Snapshot) => void; onFailure: (error: OperationFailure) => void
}) {
  const desktop = gateway.mode === 'desktop', supported = !desktop || !!snapshot.CanEditDolby
  const [baseline] = useState(() => structuredClone(snapshot.DolbyProfiles?.[device.Id] ?? null))
  const [enabled, setEnabled] = useState(baseline !== null), [draft, setDraft] = useState<DolbyProfile>(() => baseline ?? emptyDolby())
  const [job, setJob] = useState<DolbyRead | null>(null), [starting, setStarting] = useState(false), [saving, setSaving] = useState(false), [closing, setClosing] = useState(false)
  const [error, setError] = useState(''), [message, setMessage] = useState(''), [uncertain, setUncertain] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null), current = useRef<DolbyRead | null>(null), alive = useRef(true), lock = useRef(false), saveLock = useRef(false), closeRequested = useRef(false), cancelling = useRef(false)
  const running = job?.Status === 'running', blocked = saving || starting || running || closing || readOnly || !supported || uncertain
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
  return <Sheet open onOpenChange={open => { if (!open) void stop(true) }}><SheetContent className="dolby-sheet" initialFocus={heading} closeDisabled={saving || closing} closeLabel="关闭 Dolby 编辑器">
    <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">Dolby 方案</SheetTitle><SheetDescription className="sheet-description">{device.Name}{!device.Online ? ' · 离线' : ''}</SheetDescription></header>
    <div className="sheet-preview">{desktop ? '仅保存设备预设，不立即应用 Dolby' : '界面预览，操作不会改变系统设置'}</div>
    <div className="sheet-scroll">
      {!supported && <p role="status">当前后台暂不支持新 Dolby 编辑器，请使用新版后台。</p>}
      <Button variant="outline" disabled={blocked || !device.Online || snapshot.Defaults['0:1'] !== device.Id || !!snapshot.DolbyApplying} onClick={() => void start()}>{desktop ? '读取当前 Dolby 并填入' : '模拟读取 Dolby 并填入'}</Button>
      <p className="field-help">读取需要此设备是当前输出。成功后仅填入草稿；失败或取消会保留草稿。</p>
      {(starting || running || closing) && <p role="status">{closing ? '正在取消并等待读取退出…' : '正在读取 Dolby…'}</p>}
      {message && <p role={job?.Status === 'error' ? 'alert' : 'status'}>{message}</p>}
      {error && <p role="alert" className="field-help">{error}</p>}
      {running && error && <Button variant="outline" disabled={closing} onClick={() => setError('')}>重新读取状态</Button>}
      <div className="volume-toggle"><label htmlFor="use-dolby">此设备成为输出时自动应用 Dolby 方案</label><Switch id="use-dolby" checked={enabled} disabled={blocked} onCheckedChange={setEnabled} /></div>
      <fieldset className="dolby-controls" disabled={blocked || !enabled}><legend className="sr-only">Dolby 参数</legend><DolbyFields value={draft} onChange={setDraft} /></fieldset>
    </div>
    <footer className="sheet-footer"><p>仅保存留待下次使用。立即应用暂未接入。</p><div><Button variant="outline" disabled={saving || closing} onClick={() => void stop(true)}>取消</Button>{running && <Button variant="outline" disabled={closing} onClick={() => void stop(false)}>取消读取</Button>}<Button disabled={blocked} onClick={() => void save()}>{saving ? '正在保存…' : desktop ? '仅保存 Dolby' : '仅保存 Dolby（模拟）'}</Button></div></footer>
  </SheetContent></Sheet>
}
