import { useEffect, useRef, useState } from 'react'
import { Info, LoaderCircle } from 'lucide-react'
import type { Snapshot, UiGateway, UpgradeJob, UpgradeKind } from '@/data/types'
import { upgradeActive } from '@/data/upgrade'
import { Button } from './ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './ui/sheet'
import { ReleaseNotes } from './ReleaseNotes'
import { FileCheckPanel } from './FileCheckPanel'
import { UpdatePreferences } from './UpdatePreferences'

export function UpgradeSheet({ kind, gateway, snapshot, onPreference, preferenceBusy = false, onClose: finishClose }: { onPreference?: (value: boolean) => Promise<void>; preferenceBusy?: boolean; kind: UpgradeKind; gateway: UiGateway; snapshot: Snapshot; onClose: () => void }) {
  const [open, setOpen] = useState(true)
  const [savingPolicy, setSavingPolicy] = useState(false)
  const onClose = () => setOpen(false)

  const [job, setJob] = useState<UpgradeJob | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState(''), [confirm, setConfirm] = useState(false), [submitted, setSubmitted] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null), confirmation = useRef<HTMLElement>(null), current = useRef<UpgradeJob | null>(null), alive = useRef(true), lock = useRef(false), committing = useRef(false), closeRequested = useRef(false)
  const desktop = gateway.mode === 'desktop', active = upgradeActive(job), polling = active && !['available', 'repairable', 'ready'].includes(job!.Status)
  const installPending = submitted && !error && (!job || !['handoff', 'error'].includes(job.Status))
  const accept = (value: UpgradeJob) => { current.current = value; if (alive.current) setJob(value) }
  useEffect(() => { if (confirm) { confirmation.current?.focus({ preventScroll:true }); confirmation.current?.scrollIntoView?.({ block:'nearest' }) } }, [confirm])
  useEffect(() => {
    alive.current = true
    return () => { alive.current = false; if (!committing.current && upgradeActive(current.current)) void gateway.cancelUpgrade(current.current!.Token).catch(() => {}) }
  }, [gateway])
  useEffect(() => {
    if (!job || !polling || busy || error) return
    let valid = true
    const timer = window.setTimeout(() => { gateway.readUpgrade(job.Token).then(v => { if (valid) accept(v) }).catch(e => { if (valid) setError(e instanceof Error ? e.message : '暂时无法读取结果，请重新读取，勿重复提交。') }) }, 350)
    return () => { valid = false; window.clearTimeout(timer) }
  }, [gateway, job, polling, busy, error])
  async function perform(action: 'check' | 'download' | 'install' | 'cancel' | 'close') {
    if (savingPolicy || preferenceBusy) return
    if (committing.current && action !== 'close') return
    if (action === 'close' && committing.current && installPending) return
    if (lock.current) { if (action === 'close' && !committing.current) closeRequested.current = true; return }
    lock.current = true; setBusy(true); setError('')
    if (action === 'install') { committing.current = true; setSubmitted(true); setConfirm(false) }
    try {
      if (action === 'check') accept(await gateway.startUpgrade(kind))
      if ((action === 'download' || action === 'install') && current.current) accept(await gateway.upgradeAction(current.current.Token, action))
      if ((action === 'cancel' || action === 'close') && !committing.current && upgradeActive(current.current)) accept(await gateway.cancelUpgrade(current.current!.Token))
      if ((!alive.current || closeRequested.current) && !committing.current && upgradeActive(current.current)) accept(await gateway.cancelUpgrade(current.current!.Token))
      if (alive.current && (action === 'close' || closeRequested.current)) onClose()
    } catch (e) {
      if (alive.current) setError((committing.current ? '安装可能已提交，请重新读取结果，勿重复提交。' : '') + (e instanceof Error ? e.message : '操作未完成，请重试。'))
    } finally { lock.current = false; closeRequested.current = false; if (alive.current) setBusy(false) }
  }
  return <Sheet open={open} onOpenChangeComplete={value => { if (!value) finishClose() }} onOpenChange={open => { if (!open) void perform('close') }}>
    <SheetContent className="upgrade-sheet" initialFocus={heading} closeDisabled={busy || savingPolicy || installPending} closeLabel="关闭维护窗口">
      <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">{kind === 'update' ? '检查更新' : '文件检查与修复'}</SheetTitle><SheetDescription className="sr-only">检查 → 下载并校验 → 确认安装</SheetDescription></header>
      <div className="sheet-preview"><Info size={14} />{desktop ? '确认安装前不会替换程序或重启' : '界面预览，不联网或读写本机文件'}</div>
      <div className="sheet-scroll maintenance-content">
        {kind === 'update' && <UpdatePreferences includePrerelease={snapshot.Preferences.IncludePrerelease} pending={busy || savingPolicy || preferenceBusy || polling || submitted} disabled={desktop && !snapshot.CanConfigureUpdates || !onPreference} onChange={value => {
          if (lock.current || savingPolicy || !onPreference) return
          lock.current = true; setSavingPolicy(true); setError('')
          void (async () => {
            if (upgradeActive(current.current)) accept(await gateway.cancelUpgrade(current.current!.Token))
            await onPreference(value); setJob(null); current.current = null
          })().catch(e => setError(e instanceof Error ? e.message : '无法保存更新设置。')).finally(() => { lock.current = false; setSavingPolicy(false) })
        }} />}
        {kind === 'repair' && (!job || ['checking', 'passed', 'repairable', 'unavailable', 'error', 'cancelled'].includes(job.Status)) && <FileCheckPanel checking={job?.Status === 'checking' || busy && !job} result={job && job.Status !== 'checking' ? job : null} />}
        {desktop && kind === 'update' && (!snapshot.Preferences.AutoUpdateEnabled || snapshot.Preferences.GameMode) && <p className="field-help">自动更新已暂停，仍可手动更新。</p>}
        {(polling || busy) && !(kind === 'repair' && (job?.Status === 'checking' || !job)) && <p className="maintenance-progress" role="status"><LoaderCircle size={17} className="spin" />{busy ? '正在处理，请稍候…' : job?.Message}</p>}
        {error && <p role="alert" className="backend-warning">{error}</p>}
        {job && (kind === 'update' || !['checking', 'passed', 'repairable', 'unavailable', 'error', 'cancelled'].includes(job.Status)) && <section className="sheet-section" aria-live="polite">
          {!polling && !(kind === 'repair' && ['passed', 'repairable', 'unavailable', 'error', 'cancelled'].includes(job.Status)) && <h3>{job.Message}</h3>}
          {job.Version && <p>版本：{job.Version}</p>}
          {job.Status === 'downloading' && <><progress aria-label="下载进度" max={job.Total || undefined} value={job.Total ? Math.min(job.Bytes, job.Total) : undefined} /><p>{(job.Bytes / 1048576).toFixed(1)} MiB{job.Total > 0 && ` / ${(job.Total / 1048576).toFixed(1)} MiB`}</p></>}
          {kind === 'update' && job.Notes && <ReleaseNotes source={job.Notes} />}
        </section>}
        {confirm && <section ref={confirmation} tabIndex={-1} className="backend-warning upgrade-confirmation" aria-label="安装确认"><h3>{desktop ? '确认安装并重启声间？' : '模拟安装确认'}</h3><p>{desktop ? '安装助手会关闭面板和后台，备份原程序后替换文件，并重新启动。已开启的启动优先级或 Dolby 规则可能在重启后执行。' : '仅演示安装交接，不会下载、替换文件或重启程序。'}</p><p>安装交接后不能取消；失败原因和恢复结果由安装助手显示。</p></section>}
      </div>
      <footer className="sheet-footer">{submitted ? <p>安装请求已提交，请等待安装结果。</p> : job?.Status === 'downloading' && <p>取消下载会保留缓存。</p>}<div>
        <Button variant="outline" disabled={busy || savingPolicy || installPending} onClick={() => void perform('close')}>关闭</Button>
        {!active && !submitted && <Button disabled={busy || savingPolicy || preferenceBusy} onClick={() => void perform('check')}>{job ? '重新检查' : desktop ? '开始检查' : '模拟检查'}</Button>}
        {active && !submitted && <Button variant="outline" disabled={busy} onClick={() => { setConfirm(false); void perform('cancel') }}>取消任务</Button>}
        {job?.CanDownload && !submitted && <Button disabled={busy || !!error} onClick={() => void perform('download')}>{kind === 'update' ? '下载更新' : '下载修复包'}</Button>}
        {job?.Status === 'ready' && !submitted && (!confirm ? <Button disabled={busy || !!error} onClick={() => setConfirm(true)}>准备安装</Button> : <><Button variant="outline" disabled={busy} onClick={() => setConfirm(false)}>暂不安装</Button><Button disabled={busy || !!error} onClick={() => void perform('install')}>{desktop ? '确认安装并重启' : '模拟确认安装'}</Button></>)}
        {error && job && <Button variant="outline" disabled={busy} onClick={() => { setError(''); gateway.readUpgrade(job.Token).then(accept).catch(e => setError(e instanceof Error ? e.message : '暂时无法读取状态。')) }}>重新读取状态</Button>}
      </div></footer>
    </SheetContent>
  </Sheet>
}
