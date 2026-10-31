import { useEffect, useLayoutEffect, useRef, type ReactNode } from 'react'
import { CircleCheck, Download, Info, LoaderCircle, RefreshCw, ShieldCheck } from 'lucide-react'
import type { UpgradeJob } from '@/data/types'

/** Stage changes reveal content; same-stage layout changes resize without replaying the reveal. */
export function StageTransition({ stage, layoutKey, children, className = '' }: { stage: string; layoutKey?: string; children: ReactNode; className?: string }) {
  const host = useRef<HTMLDivElement>(null), previous = useRef<string | null>(null), running = useRef<Animation | null>(null), height = useRef(0)
  useLayoutEffect(() => {
    const element = host.current, first = previous.current === null, changed = previous.current !== stage
    previous.current = stage
    if (!element) return
    if (first) { height.current = element.getBoundingClientRect().height; return }
    const style = running.current?.playState === 'running' ? getComputedStyle(element) : null
    const fromHeight = style ? element.getBoundingClientRect().height : height.current
    const start: Keyframe = { opacity: style?.opacity ?? (changed ? '.65' : '1'), transform: style?.transform ?? (changed ? 'translateY(4px)' : 'translateY(0)') }
    running.current?.cancel()
    height.current = element.getBoundingClientRect().height
    const end: Keyframe = { opacity: 1, transform: 'translateY(0)' }
    if (fromHeight && height.current) { start.height = `${fromHeight}px`; end.height = `${height.current}px` }
    if (!element.animate || matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const shrinking = fromHeight > height.current
    running.current = element.animate([start, end], { duration: shrinking ? 200 : 250, easing: shrinking ? 'ease-in' : 'ease-out' })
  }, [stage, layoutKey])
  useEffect(() => {
    const query = matchMedia('(prefers-reduced-motion: reduce)')
    const stop = () => { if (query.matches) running.current?.cancel() }
    query.addEventListener('change', stop)
    return () => { query.removeEventListener('change', stop); running.current?.cancel() }
  }, [])
  return <div ref={host} className={className}>{children}</div>
}

export function UpgradeStage({ job, stage }: { job: UpgradeJob | null; stage: string }) {
  const waiting = ['checking', 'downloading', 'installing', 'cancelling', 'closing'].includes(stage)
  const labels: Record<string, string> = { idle: '检查可用更新', checking: '正在检查更新', downloading: '正在下载并校验', installing: '正在交接安装', cancelling: '正在取消任务', closing: '正在关闭' }
  const Icon = waiting ? LoaderCircle : stage === 'ready' ? ShieldCheck : stage === 'current' ? CircleCheck : stage === 'available' ? Download : stage === 'idle' ? RefreshCw : Info
  const message = job?.Status === stage ? job.Message : labels[stage] ?? job?.Message ?? labels.idle
  return <StageTransition stage={stage} layoutKey={JSON.stringify([message, job?.Version])} className="upgrade-status">
    <div className="upgrade-status-heading" role="status"><Icon size={20} className={waiting ? 'spin' : undefined} aria-hidden="true" /><div><h3>{message}</h3>{job?.Version && <p>版本：{job.Version}</p>}</div></div>
    {stage === 'downloading' && <div className="upgrade-download">
      <div className="upgrade-progress" role="progressbar" aria-label="下载进度" aria-valuemin={0} aria-valuemax={job?.Total || undefined} aria-valuenow={job?.Total ? Math.min(job.Bytes, job.Total) : undefined} data-indeterminate={!job?.Total || undefined}><span style={{ transform: `scaleX(${job?.Total ? Math.min(job.Bytes / job.Total, 1) : 0})` }} /></div>
      <p>{((job?.Bytes ?? 0) / 1048576).toFixed(1)} MiB{(job?.Total ?? 0) > 0 && ` / ${(job!.Total / 1048576).toFixed(1)} MiB`}</p>
    </div>}
  </StageTransition>
}
