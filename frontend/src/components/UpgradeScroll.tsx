import { useLayoutEffect, useRef, type ReactNode } from 'react'

/** Keep the native scroll surface, but return its reserved space until content needs it. */
export function UpgradeScroll({ children, className = 'maintenance-content' }: { children: ReactNode; className?: string }) {
  const viewport = useRef<HTMLDivElement>(null), content = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const outer = viewport.current!, inner = content.current!
    const measure = () => {
      const style = getComputedStyle(outer)
      const gutter = Math.max(0, outer.offsetWidth - outer.clientWidth - (parseFloat(style.borderLeftWidth) || 0) - (parseFloat(style.borderRightWidth) || 0))
      const available = outer.clientHeight - (parseFloat(style.paddingTop) || 0) - (parseFloat(style.paddingBottom) || 0)
      const overflow = inner.getBoundingClientRect().height > available + .5
      const space = `${gutter}px`, state = String(overflow)
      if (outer.style.getPropertyValue('--scrollbar-space') !== space) outer.style.setProperty('--scrollbar-space', space)
      if (outer.dataset.overflow !== state) outer.dataset.overflow = state
    }
    const observer = new ResizeObserver(measure)
    observer.observe(outer); observer.observe(inner)
    inner.style.transition = 'none'
    measure(); inner.getBoundingClientRect() // Settle the initial centered layout before enabling transitions.
    inner.style.removeProperty('transition')
    return () => observer.disconnect()
  }, [])
  return <div ref={viewport} className={`sheet-scroll upgrade-scroll ${className}`}><div ref={content} className="upgrade-scroll-body">{children}</div></div>
}
