import { useRef, useState } from 'react'
import { Headphones, Info, Volume2, Shield, Sparkles } from 'lucide-react'
import type { Device, DeviceProfile, DeviceRule, Snapshot } from '@/data/types'
import { SONIC_FORMAT } from '@/data/mock'
import { Button } from './ui/button'
import { Switch } from './ui/switch'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './ui/sheet'
import { DeviceIdentity } from './shared'

export function DeviceSheet({ device, snapshot, busy, onClose, onSave }: {
  device: Device; snapshot: Snapshot; busy: boolean; onClose: () => void
  onSave: (profile: DeviceProfile, rule: DeviceRule) => Promise<boolean>
}) {
  const initial = snapshot.Preferences.DeviceProfiles[device.Id] ?? { Volume: null, SpatialFormat: null }
  const heading = useRef<HTMLHeadingElement>(null)
  const [useVolume, setUseVolume] = useState(initial.Volume !== null)
  const [volume, setVolume] = useState(initial.Volume ?? 50)
  const [spatial, setSpatial] = useState(initial.SpatialFormat === null ? 'keep' : initial.SpatialFormat === '' ? 'off' : initial.SpatialFormat)
  const [rule, setRule] = useState<DeviceRule>(snapshot.Preferences.DeviceRules[device.Id] ?? 0)
  const knownSpatial = ['keep', 'off', SONIC_FORMAT].includes(spatial)
  async function save() {
    const okay = await onSave({ Volume: useVolume ? volume : null, SpatialFormat: spatial === 'keep' ? null : spatial === 'off' ? '' : spatial }, rule)
    if (okay) onClose()
  }
  return <Sheet open onOpenChange={open => { if (!open && !busy) onClose() }}>
    <SheetContent initialFocus={heading}>
      <header className="sheet-header"><SheetTitle ref={heading} tabIndex={-1} className="sheet-title">设备设置</SheetTitle><SheetDescription className="sheet-description">音量、空间音效与接入规则</SheetDescription></header>
      <div className="sheet-preview"><Info size={14} />界面预览，操作不会改变系统设置</div>
      <div className="sheet-scroll">
        <div className="sheet-device"><DeviceIdentity device={device} />{!device.Online && <span className="role-badge">离线</span>}</div>
        <section className="sheet-section"><h3><Volume2 size={16} />音量预设</h3><div className="volume-toggle"><label htmlFor="use-volume">使用指定音量</label><Switch id="use-volume" checked={useVolume} disabled={busy} onCheckedChange={setUseVolume} /></div><div className="volume-control"><Volume2 size={17} /><input type="range" aria-label="预设音量" min="0" max="100" value={volume} disabled={!useVolume || busy} onChange={e => setVolume(Number(e.target.value))} /><output>{volume}<span>%</span></output></div><p className="field-help">{useVolume ? '仅保存预设不会改变当前音量。' : '保持设备当前音量，不写入音量设置。'}</p></section>
        <section className="sheet-section"><h3><Headphones size={16} />Windows 空间音效</h3><label className="sr-only" htmlFor="spatial">空间音效预设</label><select id="spatial" value={spatial} disabled={device.Flow === 1 || busy} onChange={e => setSpatial(e.target.value)}><option value="keep">保持不变</option><option value="off">关闭空间音效</option><option value={SONIC_FORMAT}>Windows Sonic（示例选项）</option>{!knownSpatial && <option value={spatial}>保留已有音效格式</option>}</select><p className="field-help">{device.Flow === 1 ? '麦克风不适用播放空间音效；保留已有字段。' : '这里只演示选项，实际支持情况将在连接后台后检查。'}</p></section>
        <section className="sheet-section"><h3><Shield size={16} />白名单 · 免打扰规则</h3><label className="sr-only" htmlFor="device-rule">白名单规则</label><select id="device-rule" value={rule} disabled={busy} onChange={e => setRule(Number(e.target.value) as DeviceRule)}><option value="0">使用全局设置</option><option value="1">接受系统选择，不弹窗</option><option value="2">接入时自动切换，不弹窗</option></select><p className="field-help">{['沿用全局优先级与接入询问设置。', '保留 Windows 的选择和音量／空间音效，不主动切换。', '接入后主动切换到此设备并应用预设，优先于普通排序。'][rule]}</p></section>
        <div className="dolby-placeholder"><Sparkles size={17} /><div><strong>Dolby 音效</strong><p>高级编辑器将在后续阶段接入。</p></div><span className="role-badge">暂未接入</span></div>
      </div>
      <footer className="sheet-footer"><p>仅保存在本次预览中，刷新页面后重置。</p><div><Button variant="outline" disabled={busy} onClick={onClose}>取消</Button><Button disabled={busy} onClick={() => void save()}>{busy ? '正在保存…' : '仅保存（模拟）'}</Button></div></footer>
    </SheetContent>
  </Sheet>
}
