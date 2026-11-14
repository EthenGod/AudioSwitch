import { t } from '../i18n'
import { CheckDisclosure } from './CheckDisclosure'
import { CircleCheck, CircleX, Info, ShieldCheck, ShieldAlert } from 'lucide-react'

export function FileCheckResult({ status, message, directory, entries = [], entryStatuses, legacy }: {
  status: string; message: string; directory?: string; entries?: string[]; entryStatuses?: string[]; legacy?: string
}) {
  const passed = status === 'passed', failed = status === 'failed' || status === 'repairable'
  const Icon = passed ? ShieldCheck : failed ? ShieldAlert : Info
  const items = entries.map((text, index) => {
    const ok = entryStatuses?.[index] ? entryStatuses[index] === 'passed' : /^(示例)?通过[:：]/.test(text)
    const bad = entryStatuses?.[index] ? entryStatuses[index] === 'failed' : !ok && /缺失|不符|失败|损坏|无法|错误|异常|不匹配|^检查未完成[:：]/.test(text)
    return { text, ok, bad }
  }).sort((a, b) => Number(b.bad) - Number(a.bad) || Number(a.ok) - Number(b.ok))
  return <div className="file-check-result">
    <div className={`file-check-summary ${passed ? 'is-passed' : failed ? 'is-failed' : ''}`}>
      <span className="file-check-symbol"><Icon size={26} aria-hidden="true" /></span>
      <div><h3>{passed ? t("文件完整") : failed ? t("需要修复") : t("检查未完成")}</h3><p>{message}</p></div>
    </div>
    {items.length > 0 && <div className="file-check-counts" aria-label={t("检查项目统计")}>
      <span><CircleCheck size={14} />{t("{0} 项通过", items.filter(i => i.ok).length)}</span>
      {items.some(i => i.bad) && <span className="is-failed"><CircleX size={14} />{t("{0} 项异常", items.filter(i => i.bad).length)}</span>}
      {items.some(i => !i.bad && !i.ok) && <span><Info size={14} />{t("{0} 项说明", items.filter(i => !i.bad && !i.ok).length)}</span>}
    </div>}
    {items.length > 0 && <ul className="file-check-list">{items.map((item, index) => {
      const Mark = item.ok ? CircleCheck : item.bad ? CircleX : Info
      return <li key={index} className={item.ok ? 'is-passed' : item.bad ? 'is-failed' : ''}><Mark size={16} aria-hidden="true" /><span>{item.text}</span></li>
    })}</ul>}
    {directory && <CheckDisclosure label={t("检查目录")}><p>{directory}</p></CheckDisclosure>}
    {!entries.length && legacy && <CheckDisclosure label={t("检查详情")}><pre>{legacy}</pre></CheckDisclosure>}
  </div>
}
