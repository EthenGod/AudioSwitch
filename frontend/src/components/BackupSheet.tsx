import { useEffect, useRef, useState } from 'react'
import { Info } from 'lucide-react'
import type { ImportPreview, Snapshot, UiGateway } from '@/data/types'
import { OperationFailure } from '@/data/desktop'
import { Button } from './ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './ui/sheet'

export function BackupSheet({ mode, gateway, onClose, onResult, onFailure }: {
  mode: 'import' | 'export'; gateway: UiGateway; onClose: () => void
  onResult: (snapshot: Snapshot) => void; onFailure: (error: OperationFailure) => void
}) {
  const desktop = gateway.mode === 'desktop', importing = mode === 'import'
  const heading = useRef<HTMLHeadingElement>(null), locked = useRef(false), token = useRef('')
  const [busy, setBusy] = useState(false), [preview, setPreview] = useState<ImportPreview | null>(null)
  const [error, setError] = useState(''), [complete, setComplete] = useState(''), [uncertain, setUncertain] = useState(false)
  useEffect(() => () => { if (token.current) void gateway.discardImport(token.current).catch(() => { /* No write; native state also expires on exit or reselection. */ }) }, [gateway])
  async function run(operation: () => Promise<void>) {
    if (locked.current) return
    locked.current = true; setBusy(true); setError('')
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
    })
  }
  function confirm() {
    if (!preview) return
    const selected = preview.Token
    void run(async () => {
      setPreview(null); token.current = ''
      const result = await gateway.confirmImport(selected)
      onResult(result.snapshot); setComplete(result.BackupPath)
    })
  }
  return <Sheet open onOpenChange={open => { if (!open && !locked.current) onClose() }}>
    <SheetContent initialFocus={heading} closeDisabled={busy} closeLabel="关闭备份窗口">
      <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">{importing ? '导入设置' : '导出备份'}</SheetTitle><SheetDescription className="sheet-description">{importing ? '检查备份内容，再确认替换' : '保存完整设备偏好与音效预设'}</SheetDescription></header>
      <div className="sheet-preview"><Info size={14} />{desktop ? importing ? '确认前不会修改配置' : '只导出配置，不应用音效' : '界面预览，不读写文件或系统配置'}</div>
      <div className="sheet-scroll backup-content">
        {importing ? <p>导入会替换设备排序、白名单、应用偏好和音效预设。旧配置会先自动备份；不会立即切换设备或应用音效。开机自启登记不受影响。</p> : <p>备份包含离线设备和完整 Dolby 预设。请选择单独的文件保存，不能覆盖正在使用的配置。</p>}
        {!desktop && <p className="field-help">使用内置示例演示流程，没有真实文件选择或备份生成。</p>}
        {busy && <p role="status">{importing ? '正在处理备份…' : '正在选择位置并导出…'}</p>}
        {error && <p role="alert" className="backend-warning">{error}</p>}
        {uncertain && <p>请关闭此窗口，在声音设备页刷新状态后再操作。</p>}
        {preview && <section className="sheet-section"><h3>备份摘要</h3><p className="backup-path">{preview.FileName}</p><dl className="backup-summary">
          <div><dt>记录的设备</dt><dd>{preview.Devices} 台（当前离线 {preview.OfflineDevices} 台）</dd></div>
          <div><dt>设备预设</dt><dd>{preview.Profiles} 项</dd></div><div><dt>Dolby 预设</dt><dd>{preview.DolbyProfiles} 项</dd></div><div><dt>白名单规则</dt><dd>{preview.Rules} 项</dd></div>
        </dl><p className="field-help">离线设备与暂不支持的效果仍会保留。若预览后配置有变动，需要重新选择文件。</p></section>}
        {complete && <section role="status" className="sheet-section"><h3>{desktop ? importing ? '配置已导入' : '备份已导出' : '模拟流程已完成'}</h3><p>{desktop && importing ? '原配置备份位置：' : desktop ? '文件位置：' : ''}</p><p className="backup-path">{complete}</p></section>}
      </div>
      <footer className="sheet-footer"><p>{desktop ? '关闭窗口后，未确认的导入不会执行。' : '模拟操作仅影响本次预览。'}</p><div>
        <Button variant="outline" disabled={busy} onClick={onClose}>{complete ? '完成' : '取消'}</Button>
        {!complete && !uncertain && (importing ? <>
          <Button variant={preview ? 'outline' : 'default'} disabled={busy} onClick={choose}>{preview ? '重新选择' : desktop ? '选择备份文件' : '选择示例备份'}</Button>
          {preview && <Button disabled={busy} onClick={confirm}>{desktop ? '确认导入' : '确认导入（模拟）'}</Button>}
        </> : <Button disabled={busy} onClick={() => void run(async () => { const result = await gateway.exportBackup(); if (result) setComplete(result.Path) })}>{desktop ? '选择保存位置' : '模拟导出'}</Button>)}
      </div></footer>
    </SheetContent>
  </Sheet>
}
