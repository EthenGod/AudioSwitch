import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'

/** Keep one control mounted while its space opens/closes; CSS can reverse in flight. */
export function ActionSlot({ visible, children }: { visible: boolean; children: ReactNode }) {
  const content = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const element = content.current
    if (!element) return
    const measure = () => setWidth(element.getBoundingClientRect().width)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  return <div className="page-action-slot" data-visible={visible} aria-hidden={!visible || undefined} inert={!visible} style={{ width: visible ? width : 0 }}>
    <div ref={content} className="page-action-content">{children}</div>
  </div>
}
