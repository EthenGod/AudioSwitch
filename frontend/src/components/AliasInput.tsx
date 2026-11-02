import { useLayoutEffect, useRef, useState } from 'react'

const words = new Intl.Segmenter('zh', { granularity: 'word' })

function nextWordEnd(name: string, caret: number) {
  // Segment the whole name so a partially typed word keeps its original boundary.
  const remaining = [...words.segment(name)].filter(part => part.isWordLike && part.index + part.segment.length > caret)
  return remaining.length > 1 ? remaining[0].index + remaining[0].segment.length : name.length
}

function atEnd(input: HTMLInputElement) {
  return input.selectionStart === input.value.length && input.selectionEnd === input.value.length
}

export function AliasInput({ value, onChange, names, placeholder, disabled }: {
  value: string; onChange: (value: string) => void; names: string[]; placeholder: string; disabled: boolean
}) {
  const input = useRef<HTMLInputElement>(null), mirror = useRef<HTMLDivElement>(null)
  const [edited, setEdited] = useState(false), [end, setEnd] = useState(true), [composing, setComposing] = useState(false)
  const prefix = value.toLowerCase()
  const match = edited && end && !composing && !disabled && value.trim()
    ? names.find(name => name.slice(0, value.length).toLowerCase() === prefix) : undefined
  const suffix = match?.slice(value.length)
  const boundary = suffix && match ? nextWordEnd(match, value.length) : value.length
  function align() {
    if (input.current && mirror.current) mirror.current.scrollLeft = input.current.scrollLeft
  }
  useLayoutEffect(align, [value, suffix])
  return <div className="alias-input" data-suggesting={!!suffix || undefined}>
    <input ref={input} id="device-alias" type="text" autoComplete="off" aria-autocomplete="inline" spellCheck={false}
      aria-description={suffix ? `建议补全：${suffix}。按 Tab 补一个词` : undefined}
      maxLength={64} value={value} placeholder={placeholder} disabled={disabled}
      onChange={event => { setEdited(true); setEnd(atEnd(event.currentTarget)); onChange(event.currentTarget.value) }}
      onBlur={() => setEdited(false)} onSelect={event => setEnd(atEnd(event.currentTarget))} onScroll={align}
      onCompositionStart={() => setComposing(true)} onCompositionEnd={() => setComposing(false)}
      onKeyDown={event => {
        if (!suffix || !match || event.nativeEvent.isComposing || event.nativeEvent.keyCode === 229 || !atEnd(event.currentTarget)) return
        if (event.key === 'Escape') {
          event.preventDefault(); event.stopPropagation(); setEdited(false)
        } else if (event.key === 'Tab' && !event.shiftKey && !event.altKey && !event.ctrlKey && !event.metaKey) {
          event.preventDefault()
          onChange(value + match.slice(value.length, boundary))
        }
      }} />
    {suffix && match && <>
      <div className="alias-ghost" ref={mirror} aria-hidden="true"><span className="alias-prefix">{value}</span><span className="alias-next">{match.slice(value.length, boundary)}</span>{match.slice(boundary)}</div>
      <kbd className="alias-tab" aria-hidden="true">Tab</kbd>
    </>}
  </div>
}
