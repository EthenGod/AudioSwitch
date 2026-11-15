import { useState, type CSSProperties } from 'react'
import { Dialog } from '@base-ui/react/dialog'
import { Check, Languages, LoaderCircle } from 'lucide-react'
import { LANGUAGES, languageChoices, resolveLanguage, SYSTEM_LANGUAGE, translate } from '../i18n'
import { Button } from './ui/button'
import { Crossfade } from './Crossfade'

/** The control stays mounted and focused; only its visual caption crossfades. */
function LanguageCaption({ language, source }: { language: string; source: string }) {
  return <span className="language-caption" aria-hidden="true">{LANGUAGES.map(item =>
    <span key={item.id} lang={item.id} data-active={language === item.id}>{translate(item.id, source)}</span>)}</span>
}

export function LanguageDialog({ open = true, allowSystem = true, recommended, onConfirm }: { open?: boolean; allowSystem?: boolean; recommended: string; onConfirm: (language: string) => Promise<boolean> }) {
  const [chosen, setChosen] = useState(allowSystem ? SYSTEM_LANGUAGE : recommended), [saving, setSaving] = useState(false), [failed, setFailed] = useState(false)
  const language = resolveLanguage(chosen, recommended)
  const text = (source: string) => translate(language, source)
  const choices = languageChoices(allowSystem, language)
  async function confirm() {
    if (saving) return
    setSaving(true); setFailed(false)
    try { if (!await onConfirm(chosen)) setFailed(true) } catch { setFailed(true) }
    finally { setSaving(false) }
  }
  return <Dialog.Root open={open} disablePointerDismissal>
    <Dialog.Portal>
      <Dialog.Backdrop className="language-backdrop" />
      <div className="language-layer">
      <Dialog.Popup className="language-dialog" lang={language}>
        <Languages size={24} aria-hidden="true" />
        <Dialog.Title className="sr-only">{text("选择语言")}</Dialog.Title>
        <Dialog.Description className="sr-only">{text("之后可在应用设置中更改")}</Dialog.Description>
        <div aria-hidden="true"><Crossfade value={language} className="language-copy" panes={LANGUAGES.map(item => ({
          key: item.id, render: () => <div className="language-intro" lang={item.id}>
            <h2>{translate(item.id, '选择语言')}</h2><p>{translate(item.id, '之后可在应用设置中更改')}</p>
          </div>,
        }))} /></div>
        <div className="language-choices" role="radiogroup" aria-label={text("语言")}
          style={{ '--language-index': choices.findIndex(item => item.id === chosen), '--language-count': choices.length } as CSSProperties}>
          <span className="language-selection" aria-hidden="true" />
          {choices.map((item, index) => <button key={item.id} type="button" role="radio" aria-label={item.name} aria-checked={chosen === item.id}
            tabIndex={chosen === item.id ? 0 : -1} disabled={saving} lang={item.id === SYSTEM_LANGUAGE ? language : item.id} onClick={() => setChosen(item.id)}
            onKeyDown={event => {
              const next = event.key === 'Home' ? 0 : event.key === 'End' ? choices.length - 1
                : ['ArrowDown', 'ArrowRight'].includes(event.key) ? (index + 1) % choices.length
                : ['ArrowUp', 'ArrowLeft'].includes(event.key) ? (index + choices.length - 1) % choices.length : -1
              if (next < 0) return
              event.preventDefault(); setChosen(choices[next].id)
              event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role=radio]')[next].focus()
            }}>{item.id === SYSTEM_LANGUAGE ? <LanguageCaption language={language} source="跟随系统" /> : item.name}<Check className="language-check" size={16} aria-hidden="true" /></button>)}
        </div>
        {failed && <p role="alert">{text("语言未保存，请重试")}</p>}
        <Button aria-label={text("确认")} disabled={saving} onClick={() => { void confirm() }}>{saving && <LoaderCircle className="spin" size={16} />}<LanguageCaption language={language} source="确认" /></Button>
      </Dialog.Popup>
      </div>
    </Dialog.Portal>
  </Dialog.Root>
}
