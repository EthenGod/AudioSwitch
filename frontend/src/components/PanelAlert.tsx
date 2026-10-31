import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { TriangleAlert } from 'lucide-react'

/** Keep the last message through exit, independent of page navigation and refresh. */
export function PanelAlert({ message, warning = false, children }: { message: string; warning?: boolean; children?: ReactNode }) {
  const [lastMessage, setLastMessage] = useState(message)
  const region = useRef<HTMLDivElement>(null)
  const [entered, setEntered] = useState(false)
  useLayoutEffect(() => {
    // Commit the transparent frame even when mounted with an existing error.
    region.current?.getBoundingClientRect()
    setEntered(true)
  }, [])
  useLayoutEffect(() => { if (message) setLastMessage(message) }, [message])
  const open = !!message
  return <div ref={region} className="panel-alert-region" data-open={open && entered} aria-hidden={!open || undefined} inert={!open}>
    <div className="panel-alert-clip">
      <div className="panel-alert" data-tone={warning ? 'warning' : 'error'} role={open ? warning ? 'status' : 'alert' : undefined}>
        <TriangleAlert className="panel-alert-icon" size={18} aria-hidden="true" />
        <p>{message || lastMessage}</p>
        <div className="panel-alert-actions">{children}</div>
      </div>
    </div>
  </div>
}
