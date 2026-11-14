import { useState } from 'react'
import { Dialog } from '@base-ui/react/dialog'
import { Check, Languages, LoaderCircle } from 'lucide-react'
import { LANGUAGES, translate } from '../i18n'
import { Button } from './ui/button'

export function LanguageDialog({ open = true, recommended, onConfirm }: { open?: boolean; recommended: string; onConfirm: (language: string) => Promise<boolean> }) {
  const [chosen, setChosen] = useState(recommended), [saving, setSaving] = useState(false), [failed, setFailed] = useState(false)
  const text = (source: string) => translate(chosen, source)
  async function confirm() {
    if (saving) return
    setSaving(true); setFailed(false)
    try { if (!await onConfirm(chosen)) setFailed(true) } catch { setFailed(true) }
    finally { setSaving(false) }
  }
  return <Dialog.Root open={open} disablePointerDismissal>
    <Dialog.Portal>
      <Dialog.Backdrop className="language-backdrop" />
      <Dialog.Popup className="language-dialog" lang={chosen}>
        <Languages size={24} aria-hidden="true" />
        <Dialog.Title>{text("选择语言")}</Dialog.Title>
        <Dialog.Description>{text("之后可在应用设置中更改")}</Dialog.Description>
        <div className="language-choices" role="radiogroup" aria-label={text("语言")}>
          {LANGUAGES.map((item, index) => <button key={item.id} role="radio" aria-checked={chosen === item.id}
            tabIndex={chosen === item.id ? 0 : -1} disabled={saving} lang={item.id} onClick={() => setChosen(item.id)}
            onKeyDown={event => {
              const next = event.key === 'Home' ? 0 : event.key === 'End' ? LANGUAGES.length - 1
                : ['ArrowDown', 'ArrowRight'].includes(event.key) ? (index + 1) % LANGUAGES.length
                : ['ArrowUp', 'ArrowLeft'].includes(event.key) ? (index + LANGUAGES.length - 1) % LANGUAGES.length : -1
              if (next < 0) return
              event.preventDefault(); setChosen(LANGUAGES[next].id)
              event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role=radio]')[next].focus()
            }}>{item.name}{chosen === item.id && <Check size={16} />}</button>)}
        </div>
        {failed && <p role="alert">{text("语言未保存，请重试")}</p>}
        <Button disabled={saving} onClick={() => { void confirm() }}>{saving && <LoaderCircle className="spin" size={16} />}{text("确认")}</Button>
      </Dialog.Popup>
    </Dialog.Portal>
  </Dialog.Root>
}
