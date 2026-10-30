import { useEffect, useRef, useState } from 'react'
import { Info, FileInput, FileOutput, CircleCheck, LoaderCircle, Settings2, Headphones, Sparkles, ArrowLeftRight, ShieldCheck } from 'lucide-react'
import type { ImportPreview, Snapshot, UiGateway } from '@/data/types'
import { OperationFailure } from '@/data/desktop'
import { Button } from './ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './ui/sheet'
import { CheckDisclosure } from './CheckDisclosure'

export function BackupSheet({ mode, gateway, onClose: finishClose, onResult, onFailure }: {
  mode: 'import' | 'export'; gateway: UiGateway; onClose: () => void
  onResult: (snapshot: Snapshot) => void; onFailure: (error: OperationFailure) => void
}) {
  const [open, setOpen] = useState(true)
  const onClose = () => setOpen(false)

  const desktop = gateway.mode === 'desktop', importing = mode === 'import'
  const heading = useRef<HTMLHeadingElement>(null), locked = useRef(false), token = useRef('')
  const [busy, setBusy] = useState(false), [preview, setPreview] = useState<ImportPreview | null>(null)
  const [error, setError] = useState(''), [complete, setComplete] = useState(''), [uncertain, setUncertain] = useState(false)
  const [activity, setActivity] = useState('')
  useEffect(() => () => { if (token.current) void gateway.discardImport(token.current).catch(() => { /* No write; native state also expires on exit or reselection. */ }) }, [gateway])
  async function run(operation: () => Promise<void>, label: string) {
    if (locked.current) return
    locked.current = true; setBusy(true); setActivity(label); setError('')
    try { await operation() }
    catch (e) {
      setError(e instanceof Error ? e.message : '操作未完成，请重试。')
      if (e instanceof OperationFailure) { setUncertain(e.requiresRefresh); onFailure(e) }
    } finally { locked.current = false; setBusy(false) }
  }
  function choose() {
    void run(async () => {
      setPreview(null); token.current = ''
      const value = await gateway.chooseImport()
      token.current = value?.Token ?? ''; setPreview(value)
    }, '选择并读取备份')
  }
  function confirm() {
    if (!preview) return
    const selected = preview.Token
    void run(async () => {
      setPreview(null); token.current = ''
      const result = await gateway.confirmImport(selected)
      onResult(result.snapshot); setComplete(result.BackupPath)
    }, '正在导入配置')
  }
  return <Sheet open={open} onOpenChangeComplete={value => { if (!value) finishClose() }} onOpenChange={open => { if (!open && !locked.current) onClose() }}>
    <SheetContent initialFocus={heading} closeDisabled={busy} closeLabel="关闭备份窗口">
      <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">{importing ? '导入设置' : '导出备份'}</SheetTitle><SheetDescription className="sr-only">{importing ? '检查备份内容，再确认替换' : '保存完整设备偏好与音效预设'}</SheetDescription></header>
      {!desktop && <div className="sheet-preview"><Info size={14} />界面预览，不读写文件或系统配置</div>}
      <div className="sheet-scroll backup-content">
        <div className={`file-check-summary backup-hero ${complete ? 'is-passed' : ''}`}>
          <span className="file-check-symbol">{complete ? <CircleCheck size={26} /> : importing ? <FileInput size={26} /> : <FileOutput size={26} />}</span>
          <div>
            <h3 role={busy || complete ? 'status' : undefined}>{complete ? desktop ? importing ? '配置已导入' : '备份已导出' : '模拟流程已完成' : busy ? activity : preview ? preview.FileName : importing ? '选择配置备份' : '保存配置副本'}</h3>
            {!complete && !busy && <div className="backup-file-meta"><span>JSON</span>{importing && <span>最多 1 MiB</span>}</div>}
          </div>
          {busy && <LoaderCircle className="spin check-spinner" size={18} aria-hidden="true" />}
        </div>
        {error && <p role="alert" className="backend-warning">{error}</p>}
        {uncertain && <p>请关闭此窗口，在声音设备页刷新状态后再操作。</p>}
        {!complete && !preview && !busy && <ul className="backup-scope" aria-label="备份内容">
          <li><Headphones size={18} />设备与预设</li><li><Sparkles size={18} />Dolby 方案</li>
          <li><ArrowLeftRight size={18} />接入与排序</li><li><Settings2 size={18} />应用偏好</li>
        </ul>}
        {preview && <section className="backup-review" aria-label="备份摘要">
          <dl className="backup-summary">
            <div><dt>记录的设备</dt><dd>{preview.Devices}<span>台</span></dd><small>{preview.OfflineDevices} 台离线</small></div>
            <div><dt>设备预设</dt><dd>{preview.Profiles}<span>项</span></dd></div>
            <div><dt>Dolby 预设</dt><dd>{preview.DolbyProfiles}<span>项</span></dd></div>
            <div><dt>接入规则</dt><dd>{preview.Rules}<span>项</span></dd></div>
          </dl>
          <div className="backup-confirm"><ShieldCheck size={18} aria-hidden="true" /><div><strong>替换当前配置</strong><p>先备份原配置，再导入。不会切换设备或应用音效；开机自启不变。</p></div></div>
        </section>}
        {complete && <div className="backup-complete">{desktop ? <CheckDisclosure defaultOpen label={importing ? '原配置备份' : '保存位置'}><p>{complete}</p></CheckDisclosure> : <p className="field-help">{complete}</p>}</div>}
      </div>
      <footer className="sheet-footer"><div>
        <Button variant="outline" disabled={busy} onClick={onClose}>{complete ? '完成' : '取消'}</Button>
        {!complete && !uncertain && (importing ? <>
          <Button variant={preview ? 'outline' : 'default'} disabled={busy} onClick={choose}>{preview ? '重新选择' : desktop ? '选择备份文件' : '选择示例备份'}</Button>
          {preview && <Button disabled={busy} onClick={confirm}>{desktop ? '确认导入' : '确认导入（模拟）'}</Button>}
        </> : <Button disabled={busy} onClick={() => void run(async () => { const result = await gateway.exportBackup(); if (result) setComplete(result.Path) }, '选择位置并导出备份')}>{desktop ? '选择保存位置' : '模拟导出'}</Button>)}
      </div></footer>
    </SheetContent>
  </Sheet>
}
