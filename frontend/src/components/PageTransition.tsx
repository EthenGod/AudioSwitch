import { useEffect, useLayoutEffect, useRef, type ReactNode } from 'react'
import type { Page } from '@/data/types'

const order: Page[] = ['devices', 'automation', 'settings']

/** Keep one live page. Data updates never restart navigation motion. */
export function PageTransition({ page, children }: { page: Page; children: ReactNode }) {
  const host = useRef<HTMLDivElement>(null)
  const previous = useRef(page)
  const running = useRef<Animation | null>(null)
  useLayoutEffect(() => {
    const element = host.current, from = previous.current
    previous.current = page
    if (!element || from === page) return
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const active = running.current?.playState === 'running'
    const current = active ? getComputedStyle(element) : null
    const transform = current?.transform ?? `translateY(${order.indexOf(page) > order.indexOf(from) ? 8 : -8}px)`
    const opacity = current?.opacity ?? '.8'
    running.current?.cancel()
    if (reduced || !element.animate) return
    running.current = element.animate([{ transform, opacity }, { transform: 'translateY(0)', opacity: 1 }], { duration: 250, easing: 'cubic-bezier(.2,.8,.2,1)' })
  }, [page])
  useEffect(() => {
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)')
    const stop = () => { if (preference.matches) running.current?.cancel() }
    preference.addEventListener('change', stop)
    return () => { preference.removeEventListener('change', stop); running.current?.cancel() }
  }, [])
  return <div ref={host} className="page-content">{children}</div>
}
