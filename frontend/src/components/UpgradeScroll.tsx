import type { ReactNode } from 'react'
import { ScrollSurface } from './ScrollSurface'

export function UpgradeScroll({ children, className = 'maintenance-content' }: { children: ReactNode; className?: string }) {
  return <ScrollSurface className={`sheet-scroll upgrade-scroll ${className}`} bodyClassName="upgrade-scroll-body">{children}</ScrollSurface>
}
