import { render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { UpgradeScroll } from './UpgradeScroll'

it('returns the measured gutter while content fits, gives it back on overflow, and disconnects', () => {
  let measure = () => {}
  const disconnect = vi.fn(), original = globalThis.ResizeObserver
  globalThis.ResizeObserver = class { constructor(callback: () => void) { measure = callback } observe() {} unobserve() {} disconnect = disconnect } as unknown as typeof ResizeObserver
  try {
    const view = render(<UpgradeScroll><p>更新内容</p></UpgradeScroll>)
    const outer = view.container.firstElementChild as HTMLElement, inner = outer.firstElementChild as HTMLElement
    Object.defineProperties(outer, { offsetWidth: { get: () => 480 }, clientWidth: { get: () => 474 }, clientHeight: { get: () => 500 } })
    outer.style.paddingBottom = '24px'
    let height = 100
    vi.spyOn(inner, 'getBoundingClientRect').mockImplementation(() => ({ height }) as DOMRect)
    measure()
    expect(outer.style.getPropertyValue('--scrollbar-space')).toBe('6px')
    expect(outer.dataset.overflow).toBe('false')
    height = 600; measure()
    expect(outer.dataset.overflow).toBe('true')
    expect(outer.firstElementChild).toBe(inner)
    height = 100; measure()
    expect(outer.dataset.overflow).toBe('false')
    view.unmount(); expect(disconnect).toHaveBeenCalledTimes(1)
  } finally { globalThis.ResizeObserver = original }
})
