import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { Crossfade } from './Crossfade'
import { Automation } from './Automation'
import { createMockGateway } from '@/data/mock'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

it('keeps both frames for concurrent fading but only one accessible, resizes and ignores stale observers', () => {
  const observers: { measure: () => void; disconnect: ReturnType<typeof vi.fn> }[] = []
  vi.stubGlobal('ResizeObserver', class {
    disconnect = vi.fn()
    constructor(measure: () => void) { observers.push({ measure, disconnect: this.disconnect }) }
    observe() {}
  })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    return { height: Number(this.firstElementChild?.getAttribute('data-height') ?? 0) } as DOMRect
  })
  const panes = ['a', 'b'].map((key, i) => ({ key, render: () => <label data-height={100 + i * 60}>{key}<input aria-label={key} defaultValue={key} /></label> }))
  const view = render(<Crossfade value="a" panes={panes} />)
  const first = screen.getByRole('textbox'), host = view.container.firstElementChild!
  expect(host).toHaveStyle({ height: '100px' })
  fireEvent.change(first, { target: { value: '草稿' } })
  view.rerender(<Crossfade value="b" panes={panes} />)
  expect(host).toHaveStyle({ height: '160px' })
  expect(first.closest('.view-crossfade-pane')).toHaveAttribute('inert')
  expect(screen.getAllByRole('textbox')).toHaveLength(1)
  expect(screen.getByRole('textbox')).toHaveAccessibleName('b')
  act(observers[0].measure)
  expect(host).toHaveStyle({ height: '160px' })
  view.rerender(<Crossfade value="a" panes={panes} />)
  expect(screen.getByRole('textbox')).toBe(first)
  expect(first).toHaveValue('草稿')
  expect(host).toHaveAttribute('data-shrinking', 'true')
  view.unmount()
  expect(observers.every(observer => observer.disconnect.mock.calls.length > 0)).toBe(true)
})

it('switches priority views without saving and blocks actions from the fading view', async () => {
  const snapshot = await createMockGateway(0).read(), reorder = vi.fn(async () => true)
  render(<Automation snapshot={snapshot} busy={false} onPreference={vi.fn()} onReorder={reorder} />)
  const outgoing = screen.getByRole('button', { name: '下移 Studio 耳机' })
  fireEvent.click(screen.getByRole('button', { name: '麦克风输入' }))
  expect(outgoing.closest('.view-crossfade-pane')).toHaveAttribute('inert')
  fireEvent.click(outgoing)
  expect(reorder).not.toHaveBeenCalled()
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: '下移 USB 麦克风' })) })
  expect(reorder).toHaveBeenCalledExactlyOnceWith(1, [...snapshot.Preferences.DeviceOrder[1]].reverse())
  fireEvent.click(screen.getByRole('button', { name: '声音输出' }))
  expect(screen.getByRole('button', { name: '下移 Studio 耳机' })).toBe(outgoing)
})
