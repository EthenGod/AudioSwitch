import { useEffect, useRef, useState } from 'react'
import { Info, LoaderCircle } from 'lucide-react'
import type { Snapshot, UiGateway, UpgradeJob, UpgradeKind } from '@/data/types'
import { upgradeActive } from '@/data/upgrade'
import { Button } from './ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './ui/sheet'

export function UpgradeSheet({ kind, gateway, snapshot, onClose: finishClose }: { kind: UpgradeKind; gateway: UiGateway; snapshot: Snapshot; onClose: () => void }) {
  const [open, setOpen] = useState(true)
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
    <SheetContent className="upgrade-sheet" initialFocus={heading} closeDisabled={busy || installPending} closeLabel="关闭维护窗口">
      <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">{kind === 'update' ? '检查更新' : '文件检查与修复'}</SheetTitle><SheetDescription className="sheet-description">检查 → 下载并校验 → 确认安装</SheetDescription></header>
      <div className="sheet-preview"><Info size={14} />{desktop ? '确认安装前不会替换程序或重启' : '界面预览，操作不会改变系统设置'}</div>
      <div className="sheet-scroll maintenance-content">
        <p>{kind === 'update' ? '更新来源：GitHub EthenGod/AudioSwitch' : '检查程序文件；缺损时下载当前版本完整包修复。'}</p>
        <p className="field-help">{desktop ? !snapshot.Preferences.AutoUpdateEnabled ? '后台自动更新已关闭；此处只处理本次手动维护。' : snapshot.Preferences.GameMode ? '游戏模式暂停后台自动更新；此处只处理本次手动维护。' : '后台自动更新设置保持不变。' : '以下均为模拟数据，不联网、不检查本机文件。'}</p>
        {(polling || busy) && <p className="maintenance-progress" role="status"><LoaderCircle size={17} className="spin" />{busy ? '正在处理，请稍候…' : job?.Message}</p>}
        {error && <p role="alert" className="backend-warning">{error}</p>}
        {job && <section className="sheet-section" aria-live="polite">
          {!polling && <h3>{job.Message}</h3>}
          {job.Version && <p>版本：{job.Version}</p>}
          {job.Status === 'downloading' && <><progress aria-label="下载进度" max={job.Total || undefined} value={job.Total ? Math.min(job.Bytes, job.Total) : undefined} /><p>{(job.Bytes / 1048576).toFixed(1)} MiB{job.Total > 0 && ` / ${(job.Total / 1048576).toFixed(1)} MiB`}，校验完成后才能安装</p></>}
          {job.Notes && <pre className="maintenance-notes">{job.Notes}</pre>}
        </section>}
        {confirm && <section ref={confirmation} tabIndex={-1} className="backend-warning upgrade-confirmation" aria-label="安装确认"><h3>{desktop ? '确认安装并重启声间？' : '模拟安装确认'}</h3><p>{desktop ? '安装助手会关闭面板和后台，备份原程序后替换文件，并重新启动。已开启的启动优先级或 Dolby 规则可能在重启后执行。' : '仅演示安装交接，不会下载、替换文件或重启程序。'}</p><p>安装交接后不能取消；失败原因和恢复结果由安装助手显示。</p></section>}
      </div>
      <footer className="sheet-footer"><p>{submitted ? '已提交本次请求；交接不等于安装成功。' : '下载阶段可取消，缓存会保留。'}</p><div>
        <Button variant="outline" disabled={busy || installPending} onClick={() => void perform('close')}>关闭</Button>
        {!active && !submitted && <Button disabled={busy} onClick={() => void perform('check')}>{job ? '重新检查' : desktop ? '开始检查' : '模拟检查'}</Button>}
        {active && !submitted && <Button variant="outline" disabled={busy} onClick={() => { setConfirm(false); void perform('cancel') }}>取消任务</Button>}
        {job?.CanDownload && !submitted && <Button disabled={busy || !!error} onClick={() => void perform('download')}>{kind === 'update' ? '下载更新' : '下载修复包'}</Button>}
        {job?.Status === 'ready' && !submitted && (!confirm ? <Button disabled={busy || !!error} onClick={() => setConfirm(true)}>准备安装</Button> : <><Button variant="outline" disabled={busy} onClick={() => setConfirm(false)}>暂不安装</Button><Button disabled={busy || !!error} onClick={() => void perform('install')}>{desktop ? '确认安装并重启' : '模拟确认安装'}</Button></>)}
        {error && job && <Button variant="outline" disabled={busy} onClick={() => { setError(''); gateway.readUpgrade(job.Token).then(accept).catch(e => setError(e instanceof Error ? e.message : '暂时无法读取状态。')) }}>重新读取状态</Button>}
      </div></footer>
    </SheetContent>
  </Sheet>
}
