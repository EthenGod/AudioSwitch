import { useId, useState, type ReactNode } from 'react'
import { ChevronDown, Folder } from 'lucide-react'

export function CheckDisclosure({ label, children, defaultOpen = false }: { label: string; children: ReactNode; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen), id = useId()
  return <div className="check-disclosure" data-open={open}>
    <button type="button" aria-expanded={open} aria-controls={id} onClick={() => setOpen(!open)}>
      <Folder size={16} aria-hidden="true" /><span>{label}</span><ChevronDown size={16} aria-hidden="true" />
    </button>
    <div className="check-disclosure-reveal" id={id} inert={!open} aria-hidden={!open}><div><div className="check-disclosure-body">{children}</div></div></div>
  </div>
}
