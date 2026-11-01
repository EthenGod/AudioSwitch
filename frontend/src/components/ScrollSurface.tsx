import { useLayoutEffect, useRef, type ReactNode, type RefObject } from 'react'

/** Native scrolling with a persistent visual thumb, so its last frame can fade out. */
export function ScrollSurface({ children, className, bodyClassName = '', main = false, viewportRef, id }: {
  children: ReactNode; className: string; bodyClassName?: string; main?: boolean
  viewportRef?: RefObject<HTMLElement | null>; id?: string
}) {
  const frame = useRef<HTMLDivElement>(null), viewport = useRef<HTMLElement | null>(null)
  const content = useRef<HTMLDivElement>(null), track = useRef<HTMLDivElement>(null), thumb = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const root = frame.current!, outer = viewport.current!, inner = content.current!, rail = track.current!, grip = thumb.current!
    let overflow = false, range = 0, travel = 0, size = 0
    let drag: { id: number; y: number; scroll: number } | null = null
    const updateThumb = () => {
      if (overflow) grip.style.transform = `translateY(${Math.max(0, Math.min(1, outer.scrollTop / range)) * travel}px)`
    }
    const measure = () => {
      const style = getComputedStyle(outer)
      const available = outer.clientHeight - (parseFloat(style.paddingTop) || 0) - (parseFloat(style.paddingBottom) || 0)
      overflow = outer.clientHeight > 0 && inner.getBoundingClientRect().height > available + .5
      root.dataset.overflow = outer.dataset.overflow = String(overflow)
      if (overflow) {
        range = Math.max(1, outer.scrollHeight - outer.clientHeight)
        const length = rail.clientHeight
        size = Math.min(length, Math.max(24, length * outer.clientHeight / outer.scrollHeight))
        travel = Math.max(0, length - size)
        grip.style.height = `${size}px`
        updateThumb()
      } else {
        // Preserve the last thumb geometry throughout its fade-out.
        drag = null
      }
    }
    const move = (event: PointerEvent) => {
      if (!drag || drag.id !== event.pointerId || !overflow || travel <= 0) return
      outer.scrollTop = Math.max(0, Math.min(range, drag.scroll + (event.clientY - drag.y) * range / travel))
      updateThumb()
    }
    const down = (event: PointerEvent) => {
      if (event.button !== 0 || !overflow || travel <= 0) return
      event.preventDefault() // Dragging must not steal the editing focus.
      if (event.target !== grip) {
        outer.scrollTop = Math.max(0, Math.min(range, (event.clientY - rail.getBoundingClientRect().top - size / 2) * range / travel))
        updateThumb()
      }
      drag = { id: event.pointerId, y: event.clientY, scroll: outer.scrollTop }
      rail.setPointerCapture(event.pointerId)
    }
    const end = (event: PointerEvent) => {
      if (drag?.id !== event.pointerId) return
      drag = null
      if (rail.hasPointerCapture(event.pointerId)) rail.releasePointerCapture(event.pointerId)
    }
    const wheel = (event: WheelEvent) => {
      if (!overflow || event.ctrlKey || event.shiftKey) return
      event.preventDefault()
      outer.scrollTop += event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? outer.clientHeight : 1)
      updateThumb()
    }
    const observer = new ResizeObserver(measure)
    observer.observe(outer); observer.observe(inner)
    measure(); inner.getBoundingClientRect()
    root.dataset.ready = 'true'
    outer.addEventListener('scroll', updateThumb, { passive: true })
    rail.addEventListener('pointerdown', down); rail.addEventListener('pointermove', move)
    rail.addEventListener('pointerup', end); rail.addEventListener('pointercancel', end); rail.addEventListener('lostpointercapture', end)
    rail.addEventListener('wheel', wheel, { passive: false })
    return () => {
      observer.disconnect()
      outer.removeEventListener('scroll', updateThumb)
      rail.removeEventListener('pointerdown', down); rail.removeEventListener('pointermove', move)
      rail.removeEventListener('pointerup', end); rail.removeEventListener('pointercancel', end); rail.removeEventListener('lostpointercapture', end)
      rail.removeEventListener('wheel', wheel)
    }
  }, [])
  const Viewport = main ? 'main' : 'div'
  return <div ref={frame} className="scroll-surface">
    <Viewport ref={element => { viewport.current = element; if (viewportRef) viewportRef.current = element }} id={id} tabIndex={main ? -1 : 0} className={`scroll-viewport ${className}`}>
      <div ref={content} className={`scroll-body ${bodyClassName}`}>{children}</div>
    </Viewport>
    <div ref={track} className="scroll-rail" aria-hidden="true"><div ref={thumb} className="scroll-thumb" /></div>
  </div>
}
