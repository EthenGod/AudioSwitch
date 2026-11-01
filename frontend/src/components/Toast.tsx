import { useLayoutEffect, useRef, useState } from 'react'
import { Check, TriangleAlert, X } from 'lucide-react'
import { Button } from './ui/button'

export type Notice = { text: string; error?: boolean } | null

/** Keep the previous content mounted so dismissal and reopening can reverse smoothly. */
export function Toast({ notice, onClose }: { notice: Notice; onClose: () => void }) {
  const [last, setLast] = useState(notice)
  const [entered, setEntered] = useState(false)
  const element = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => { element.current?.getBoundingClientRect(); setEntered(true) }, [])
  useLayoutEffect(() => { if (notice) setLast(notice) }, [notice])
  const content = notice ?? last
  const open = !!notice
  return <div ref={element} className={`toast ${content?.error ? 'toast-error' : ''}`} data-open={open && entered} aria-hidden={!open || undefined} inert={!open} role={open ? content?.error ? 'alert' : 'status' : undefined}>
    {content?.error ? <TriangleAlert size={17} aria-hidden="true" /> : <Check size={17} aria-hidden="true" />}
    <span>{content?.text.trim().replace(/。+$/u, '')}</span>
    <Button variant="ghost" size="icon" aria-label="关闭操作提示" onClick={onClose}><X /></Button>
  </div>
}
