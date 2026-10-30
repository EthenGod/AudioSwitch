import { useEffect, useLayoutEffect, useRef } from 'react'
import { FileSearch, FileCode, Monitor, Package, LoaderCircle } from 'lucide-react'
import { FileCheckResult } from './FileCheckResult'

export function FileCheckPanel({ checking, result }: {
  checking: boolean
  result?: { Status: string; Message: string; Directory?: string; Entries?: string[]; Notes?: string } | null
}) {
  const phase = checking ? 'checking' : result ? 'result' : 'idle'
  const host = useRef<HTMLDivElement>(null), lastHeight = useRef(0), motion = useRef<Animation | null>(null)
  useEffect(() => {
    const query = matchMedia('(prefers-reduced-motion: reduce)')
    const stop = () => { if (query.matches) motion.current?.cancel() }
    query.addEventListener('change', stop)
    return () => { query.removeEventListener('change', stop); motion.current?.cancel() }
  }, [])
  useLayoutEffect(() => {
    const node = host.current
    if (!node) return
    const from = motion.current?.playState === 'running' ? node.getBoundingClientRect().height : lastHeight.current
    motion.current?.cancel()
    const to = node.getBoundingClientRect().height
    lastHeight.current = to
    if (from && from !== to && node.animate && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
      motion.current = node.animate([{ height: `${from}px` }, { height: `${to}px` }], { duration: 250, easing: 'ease-out' })
    }
  }, [phase])
  return <div ref={host} className="file-check-panel" aria-busy={checking}>
    {phase === 'result' && result ? <FileCheckResult status={result.Status} message={result.Message} directory={result.Directory} entries={result.Entries} legacy={result.Notes} /> :
      <div className={`file-check-welcome ${checking ? 'is-checking' : ''}`}>
        <div className="file-check-summary">
          <span className="file-check-symbol"><FileSearch size={26} aria-hidden="true" /></span>
          <h3 role={checking ? 'status' : undefined}>{checking ? '正在检查' : '程序文件检查'}</h3>
          {checking && <LoaderCircle size={18} className="spin check-spinner" aria-hidden="true" />}
        </div>
        <ul className="file-check-scope" aria-label="检查范围">
          {[[FileCode, '主程序'], [Monitor, '桌面面板'], [Package, '运行组件']].map(([Icon, label]) => {
            const Symbol = Icon as typeof FileCode
            return <li key={String(label)}><Symbol size={18} aria-hidden="true" /><span>{String(label)}</span><span className="check-scan" aria-hidden="true" /></li>
          })}
        </ul>
      </div>}
  </div>
}
