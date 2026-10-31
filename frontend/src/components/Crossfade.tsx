import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'

/** Fixed views overlap during a fade; only the selected view can receive input. */
export function Crossfade({ value, panes, className = '' }: {
  value: string; panes: { key: string; render: (active: boolean) => ReactNode }[]; className?: string
}) {
  const host = useRef<HTMLDivElement>(null)
  const [size, setSize] = useState<{ height: number; shrinking: boolean } | null>(null)
  useLayoutEffect(() => {
    const content = host.current?.querySelector<HTMLElement>('[data-crossfade-active="true"]')
    if (!content) return
    let observing = true
    const measure = () => {
      if (!observing) return
      const height = content.getBoundingClientRect().height
      setSize(previous => previous?.height === height ? previous : { height, shrinking: !!previous && height < previous.height })
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(content)
    return () => { observing = false; observer.disconnect() }
  }, [value])
  return <div ref={host} className={`view-crossfade ${className}`} data-shrinking={size?.shrinking || undefined} style={{ height: size?.height }}>
    {panes.map(pane => {
      const active = pane.key === value
      return <div key={pane.key} className="view-crossfade-pane" data-crossfade-active={active} aria-hidden={!active || undefined} inert={!active}>
        {pane.render(active)}
      </div>
    })}
  </div>
}
