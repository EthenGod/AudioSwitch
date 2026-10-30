import { Globe } from 'lucide-react'
import { Select } from './ui/select'

export function UpdatePreferences({ includePrerelease, pending, disabled, onChange }: {
  includePrerelease: boolean; pending: boolean; disabled: boolean; onChange: (value: boolean) => void
}) {
  return <section className="update-preferences" aria-label="更新设置">
    <div><span><Globe size={16} aria-hidden="true" />更新渠道</span><Select label="更新渠道" value="GitHub" options={[{ value: 'GitHub', label: 'GitHub' }]} onValueChange={() => {}} disabled={pending} /></div>
    <div><span>接收版本</span><Select label="接收版本" value={includePrerelease ? 'preview' : 'stable'} options={[{ value: 'stable', label: '仅正式版' }, { value: 'preview', label: '包含测试版' }]} pending={pending} disabled={disabled} onValueChange={value => onChange(value === 'preview')} /></div>
    {disabled && <p className="field-help">连接新版后台后可更改接收版本。</p>}
  </section>
}

