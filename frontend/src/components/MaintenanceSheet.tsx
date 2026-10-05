import { useEffect, useRef, useState } from 'react'
import { Info, LoaderCircle } from 'lucide-react'
import type { MaintenanceJob, MaintenanceKind, Snapshot, UiGateway } from '@/data/types'
import { Button } from './ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './ui/sheet'

export function MaintenanceSheet({ kind, gateway, snapshot, onClose }: {
  kind: MaintenanceKind; gateway: UiGateway; snapshot: Snapshot; onClose: () => void
}) {
  const desktop = gateway.mode === 'desktop', updating = kind === 'update'
  const [job, setJob] = useState<MaintenanceJob | null>(null), [starting, setStarting] = useState(false), [closing, setClosing] = useState(false), [error, setError] = useState('')
  const heading = useRef<HTMLHeadingElement>(null), current = useRef<MaintenanceJob | null>(null), alive = useRef(true), lock = useRef(false), closeRequested = useRef(false)
  const running = job?.Status === 'running'
  function accept(value: MaintenanceJob) { current.current = value; if (alive.current) setJob(value) }
  useEffect(() => {
    alive.current = true
    return () => { alive.current = false; if (current.current?.Status === 'running') void gateway.cancelMaintenance(current.current.Token).catch(() => {}) }
  }, [gateway])
  useEffect(() => {
    if (!job || !running || closing || error) return
    let active = true
    const timer = window.setTimeout(() => {
      gateway.readMaintenance(job.Token).then(value => { if (active) accept(value) }).catch(e => { if (active) setError(e instanceof Error ? e.message : '无法读取检查状态，请重试或取消。') })
    }, 400)
    return () => { active = false; window.clearTimeout(timer) }
  }, [gateway, job, running, closing, error])
  async function stop(close: boolean) {
    if (lock.current) { if (close) { closeRequested.current = true; setClosing(true) }; return }
    lock.current = true; setClosing(true); setError('')
    try {
      if (current.current?.Status === 'running') accept(await gateway.cancelMaintenance(current.current.Token))
      if (close && alive.current) onClose()
    } catch (e) { if (alive.current) setError(e instanceof Error ? e.message : '未能确认取消，请重试。') }
    finally { lock.current = false; closeRequested.current = false; if (alive.current) setClosing(false) }
  }
  async function start() {
    if (lock.current || current.current?.Status === 'running') return
    lock.current = true; setStarting(true); setError('')
    try {
      const value = await gateway.startMaintenance(kind); accept(value)
      if (!alive.current || closeRequested.current) {
        if (value.Status === 'running') accept(await gateway.cancelMaintenance(value.Token))
        if (alive.current) onClose()
      }
    } catch (e) { if (alive.current) setError(e instanceof Error ? e.message : '无法开始检查，请重试。') }
    finally { lock.current = false; closeRequested.current = false; if (alive.current) { setStarting(false); setClosing(false) } }
  }
  return <Sheet open onOpenChange={open => { if (!open) void stop(true) }}>
    <SheetContent initialFocus={heading} closeDisabled={closing} closeLabel="关闭检查窗口">
      <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">{updating ? '检查更新' : '文件检查'}</SheetTitle><SheetDescription className="sheet-description">{updating ? '查看正式版本与更新说明' : '检查后台运行必需文件'}</SheetDescription></header>
      <div className="sheet-preview"><Info size={14} />{desktop ? '只读检查，不安装、修复或修改配置' : '界面预览，不联网或读取本机文件'}</div>
      <div className="sheet-scroll maintenance-content">
        {updating ? <>
          <p>更新来源：GitHub · EthenGod/AudioSwitch</p>
          <p className="field-help">{!snapshot.Preferences.AutoUpdateEnabled ? '后台自动更新已关闭。手动检查仍可使用。' : snapshot.Preferences.GameMode ? '游戏模式暂停后台自动更新。手动检查仍可使用。' : '后台自动更新设置保持不变。'}</p>
          <p className="field-help">新面板尚未接入完整更新包，下载和安装暂未开放。</p>
        </> : <p className="field-help">这里只检查后台程序、运行配置和音效辅助工具。新面板与 WebView2 的发布校验将在后续接入；本次不修复任何文件。</p>}
        {(starting || running || closing) && <p role="status" className="maintenance-progress"><LoaderCircle className="spin" size={17} />{closing ? '正在取消并等待检查退出…' : starting ? '正在启动检查…' : job?.Message}</p>}
        {error && <p role="alert" className="backend-warning">{error}</p>}
        {job && job.Status !== 'running' && <section className="sheet-section" aria-live="polite">
          <h3>{job.Message}</h3>
          {job.CurrentVersion && <p>当前后台版本：v{job.CurrentVersion}</p>}
          {job.LatestVersion && <p>最新正式版本：{job.LatestVersion}</p>}
          {job.Directory && <p className="backup-path">检查目录：{job.Directory}</p>}
          {!!job.Entries?.length && <ul className="maintenance-entries">{job.Entries.map((entry, index) => <li key={index}>{entry}</li>)}</ul>}
          {job.Notes && <><h3>更新说明</h3><pre className="maintenance-notes">{job.Notes}</pre></>}
        </section>}
      </div>
      <footer className="sheet-footer"><p>{desktop ? '关闭窗口会停止本次检查。' : '示例结果不代表本机或实际版本状态。'}</p><div>
        <Button variant="outline" disabled={closing} onClick={() => void stop(true)}>关闭</Button>
        {running || starting ? <Button disabled={closing || starting} onClick={() => void stop(false)}>取消检查</Button> : <Button disabled={closing} onClick={() => void start()}>{job ? '重新检查' : desktop ? '开始检查' : '模拟检查'}</Button>}
        {running && error && <Button variant="outline" disabled={closing} onClick={() => setError('')}>重新读取状态</Button>}
      </div></footer>
    </SheetContent>
  </Sheet>
}
