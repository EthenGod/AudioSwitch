import { render } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { PageTransition } from './PageTransition'

afterEach(() => vi.restoreAllMocks())
it('animates only navigation, cancels rapid transitions, and releases animation on unmount', () => {
  const cancel = vi.fn()
  const animate = vi.fn(() => ({ cancel, playState: 'running' }))
  const original = HTMLElement.prototype.animate
  HTMLElement.prototype.animate = animate as unknown as typeof original
  try {
    const view = render(<PageTransition page="devices">设备</PageTransition>)
    expect(animate).not.toHaveBeenCalled()
    view.rerender(<PageTransition page="settings">设置</PageTransition>)
    expect(animate).toHaveBeenCalledTimes(1)
    view.rerender(<PageTransition page="settings">已保存</PageTransition>)
    expect(animate).toHaveBeenCalledTimes(1)
    view.rerender(<PageTransition page="automation">自动切换</PageTransition>)
    expect(animate).toHaveBeenCalledTimes(2)
    expect(cancel).toHaveBeenCalledTimes(1)
    view.unmount(); expect(cancel).toHaveBeenCalledTimes(2)
  } finally { HTMLElement.prototype.animate = original }
})
it('changes pages immediately when reduced motion is requested', () => {
  vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: true, addEventListener() {}, removeEventListener() {} } as unknown as MediaQueryList)
  const view = render(<PageTransition page="devices">设备</PageTransition>)
  view.rerender(<PageTransition page="settings">设置</PageTransition>)
  expect(view.container).toHaveTextContent('设置')
})
