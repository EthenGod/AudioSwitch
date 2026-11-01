import { fireEvent, render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { UpgradeScroll } from './UpgradeScroll'

it('retains the last thumb during exit, returns space, and reverses without replacing the viewport', () => {
  let measure = () => {}
  const disconnect = vi.fn(), original = globalThis.ResizeObserver
  globalThis.ResizeObserver = class { constructor(callback: () => void) { measure = callback } observe() {} unobserve() {} disconnect = disconnect } as unknown as typeof ResizeObserver
  try {
    const view = render(<UpgradeScroll><p>更新内容</p></UpgradeScroll>)
    const frame = view.container.firstElementChild as HTMLElement
    const outer = frame.querySelector('.sheet-scroll') as HTMLElement, inner = outer.firstElementChild as HTMLElement
    const rail = frame.querySelector('.scroll-rail') as HTMLElement, thumb = rail.firstElementChild as HTMLElement
    Object.defineProperties(outer, { clientHeight: { get: () => 500 }, scrollHeight: { get: () => 1000 } })
    Object.defineProperty(rail, 'clientHeight', { get: () => 480 })
    let height = 100
    vi.spyOn(inner, 'getBoundingClientRect').mockImplementation(() => ({ height }) as DOMRect)
    measure()
    expect(frame.dataset.overflow).toBe('false')
    height = 1000; measure()
    expect(frame.dataset.overflow).toBe('true')
    expect(thumb.style.height).toBe('240px')
    outer.scrollTop = 250; fireEvent.scroll(outer)
    expect(thumb.style.transform).toBe('translateY(120px)')
    height = 100; measure()
    expect(frame.dataset.overflow).toBe('false')
    expect(thumb.style.height).toBe('240px')
    expect(thumb.style.transform).toBe('translateY(120px)')
    height = 1000; measure()
    expect(frame.querySelector('.sheet-scroll')).toBe(outer)
    expect(frame.dataset.overflow).toBe('true')
    view.unmount(); expect(disconnect).toHaveBeenCalledTimes(1)
  } finally { globalThis.ResizeObserver = original }
})

it('drags to both ends, cancels cleanly, and handles wheel input without taking focus', () => {
  let measure = () => {}
  const original = globalThis.ResizeObserver
  globalThis.ResizeObserver = class { constructor(callback: () => void) { measure = callback } observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver
  try {
    const view = render(<UpgradeScroll><input aria-label="草稿" /></UpgradeScroll>)
    const outer = view.container.querySelector('.sheet-scroll') as HTMLElement
    const inner = outer.firstElementChild as HTMLElement, rail = view.container.querySelector('.scroll-rail') as HTMLElement
    const thumb = rail.firstElementChild as HTMLElement, input = inner.querySelector('input')!
    Object.defineProperties(outer, { clientHeight: { get: () => 500 }, scrollHeight: { get: () => 1000 } })
    Object.defineProperty(rail, 'clientHeight', { get: () => 480 })
    vi.spyOn(inner, 'getBoundingClientRect').mockReturnValue({ height: 1000 } as DOMRect)
    vi.spyOn(rail, 'getBoundingClientRect').mockReturnValue({ top: 0 } as DOMRect)
    rail.setPointerCapture = vi.fn(); rail.hasPointerCapture = () => true; rail.releasePointerCapture = vi.fn()
    measure(); input.focus()
    const pointer = (target: HTMLElement, type: string, y: number) => {
      const event = new Event(type, { bubbles: true, cancelable: true })
      Object.assign(event, { button: 0, pointerId: 1, clientY: y })
      fireEvent(target, event)
      return event
    }
    expect(pointer(thumb, 'pointerdown', 0).defaultPrevented).toBe(true)
    expect(input).toHaveFocus()
    pointer(rail, 'pointermove', 1000); expect(outer.scrollTop).toBe(500)
    pointer(rail, 'pointermove', -100); expect(outer.scrollTop).toBe(0)
    pointer(rail, 'pointercancel', -100)
    pointer(rail, 'pointermove', 1000); expect(outer.scrollTop).toBe(0)
    pointer(rail, 'pointerdown', 360); expect(outer.scrollTop).toBe(500)
    pointer(rail, 'pointerup', 360)
    fireEvent.wheel(rail, { deltaY: -2, deltaMode: 1 }); expect(outer.scrollTop).toBe(468)
    fireEvent.wheel(rail, { deltaY: 100, ctrlKey: true }); expect(outer.scrollTop).toBe(468)
  } finally { globalThis.ResizeObserver = original }
})
