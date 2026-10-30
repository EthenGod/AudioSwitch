import { CheckDisclosure } from './CheckDisclosure'
import { CircleCheck, CircleX, Info, ShieldCheck, ShieldAlert } from 'lucide-react'

export function FileCheckResult({ status, message, directory, entries = [], legacy }: {
  status: string; message: string; directory?: string; entries?: string[]; legacy?: string
}) {
  const passed = status === 'passed', failed = status === 'failed' || status === 'repairable'
  const Icon = passed ? ShieldCheck : failed ? ShieldAlert : Info
  const items = entries.map(text => {
    const ok = /^(示例)?通过[:：]/.test(text)
    const bad = !ok && /缺失|不符|失败|损坏|无法|错误|异常|不匹配/.test(text)
    return { text, ok, bad }
  }).sort((a, b) => Number(b.bad) - Number(a.bad) || Number(a.ok) - Number(b.ok))
  return <div className="file-check-result">
    <div className={`file-check-summary ${passed ? 'is-passed' : failed ? 'is-failed' : ''}`}>
      <span className="file-check-symbol"><Icon size={26} aria-hidden="true" /></span>
      <div><h3>{passed ? '文件完整' : failed ? '需要修复' : '检查未完成'}</h3><p>{message}</p></div>
    </div>
    {items.length > 0 && <div className="file-check-counts" aria-label="检查项目统计">
      <span><CircleCheck size={14} />{items.filter(i => i.ok).length} 项通过</span>
      {items.some(i => i.bad) && <span className="is-failed"><CircleX size={14} />{items.filter(i => i.bad).length} 项异常</span>}
      {items.some(i => !i.bad && !i.ok) && <span><Info size={14} />{items.filter(i => !i.bad && !i.ok).length} 项说明</span>}
    </div>}
    {items.length > 0 && <ul className="file-check-list">{items.map((item, index) => {
      const Mark = item.ok ? CircleCheck : item.bad ? CircleX : Info
      return <li key={index} className={item.ok ? 'is-passed' : item.bad ? 'is-failed' : ''}><Mark size={16} aria-hidden="true" /><span>{item.text}</span></li>
    })}</ul>}
    {directory && <CheckDisclosure label="检查目录"><p>{directory}</p></CheckDisclosure>}
    {!entries.length && legacy && <CheckDisclosure label="检查详情"><pre>{legacy}</pre></CheckDisclosure>}
  </div>
}
