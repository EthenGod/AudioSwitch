import { act, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import type { Device } from '@/data/types'
import { DeviceListMotion } from './DeviceListMotion'

const output: Device = { Id: 'out', Name: '同名设备', Flow: 0, Online: true, Kind: 'speaker', Connection: 'USB' }
const input: Device = { ...output, Id: 'in', Flow: 1, Kind: 'microphone' }
function List({ devices }: { devices: Device[] }) {
  return <div role="table" tabIndex={-1}><DeviceListMotion devices={devices}>{device =>
    <div role="row" data-testid={device.Id}><span role="cell">{device.Online ? '在线' : '离线'}</span><button>{device.Id}</button></div>
  }</DeviceListMotion></div>
}
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks() })

it('starts row movement and container resizing immediately while outgoing rows are still fading', () => {
  vi.useFakeTimers()
  const original = HTMLElement.prototype.animate
  const animations: { cancel: ReturnType<typeof vi.fn>; onfinish: (() => void) | null }[] = []
  const calls: { element: HTMLElement; frames: Keyframe[] }[] = []
  HTMLElement.prototype.animate = function (frames) {
    calls.push({ element: this, frames: frames as Keyframe[] })
    const animation = { cancel: vi.fn(), onfinish: null }
    animations.push(animation)
    return animation as unknown as Animation
  }
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const group = this.classList.contains('device-motion-list')
    const siblings = this.parentElement ? Array.from(this.parentElement.children) as HTMLElement[] : []
    const index = siblings.filter(child => child.dataset.present !== 'false').indexOf(this)
    const top = this.dataset.present === 'false' ? Number.parseFloat(this.style.top) || 0 : index * 56
    return { top: group ? 0 : top, height: group ? Array.from(this.children).filter(child => (child as HTMLElement).dataset.present !== 'false').length * 56 : 56 } as DOMRect
  })
  try {
    const view = render(<List devices={[output, input]} />)
    const retained = screen.getByTestId('in').closest('.device-motion-row')!
    view.rerender(<List devices={[input]} />)
    expect(screen.getByTestId('out')).toBeInTheDocument()
    expect(calls.find(call => call.element === retained)?.frames).toEqual([
      { transform: 'translateY(56px)' }, { transform: 'translateY(0)' },
    ])
    expect(calls.find(call => call.element.classList.contains('device-motion-list'))?.frames).toEqual([
      { height: '112px' }, { height: '56px' },
    ])
    const count = calls.length
    act(() => { vi.advanceTimersByTime(200) })
    expect(screen.queryByTestId('out')).not.toBeInTheDocument()
    expect(calls).toHaveLength(count)
    view.rerender(<List devices={[{ ...input, Online: false }]} />)
    expect(calls).toHaveLength(count)
    view.unmount()
    expect(animations.every(animation => animation.cancel.mock.calls.length > 0)).toBe(true)
  } finally { HTMLElement.prototype.animate = original }
})

it('fades departing rows out, immediately disables them and preserves retained same-name rows', () => {
  vi.useFakeTimers()
  const view = render(<List devices={[output, input]} />)
  const row = screen.getByTestId('out'), retained = screen.getByTestId('in')
  screen.getByRole('button', { name: 'out' }).focus()
  view.rerender(<List devices={[input]} />)
  expect(row.closest('.device-motion-row')).toHaveAttribute('data-visible', 'false')
  expect(row.closest('.device-motion-row')).toHaveAttribute('inert')
  expect(screen.getAllByRole('row')).toEqual([retained])
  expect(screen.getByRole('table')).toHaveFocus()
  act(() => { vi.advanceTimersByTime(199) })
  expect(row).toBeInTheDocument()
  act(() => { vi.advanceTimersByTime(1) })
  expect(row).not.toBeInTheDocument()
  expect(screen.getByTestId('in')).toBe(retained)
})

it('reverses a fast filter change on the same nodes and cancels stale removal', () => {
  vi.useFakeTimers()
  const view = render(<List devices={[output, input]} />)
  const row = screen.getByTestId('out')
  view.rerender(<List devices={[input]} />)
  act(() => { vi.advanceTimersByTime(90) })
  view.rerender(<List devices={[output, input]} />)
  act(() => { vi.advanceTimersByTime(300) })
  expect(screen.getByTestId('out')).toBe(row)
  expect(row.closest('.device-motion-row')).toHaveAttribute('data-visible', 'true')
  expect(row.closest('.device-motion-row')).not.toHaveAttribute('inert')
  expect(screen.getAllByRole('row')).toEqual([row, screen.getByTestId('in')])
})

it('handles hotplug, empty lists and offline snapshots without replacing surviving rows', () => {
  vi.useFakeTimers()
  const view = render(<List devices={[output]} />)
  const row = screen.getByTestId('out')
  view.rerender(<List devices={[{ ...output, Online: false }, input]} />)
  expect(screen.getByTestId('out')).toBe(row)
  expect(row).toHaveTextContent('离线')
  expect(screen.getByTestId('in').closest('.device-motion-row')).toHaveAttribute('data-visible', 'true')
  view.rerender(<List devices={[]} />)
  expect(screen.queryAllByRole('row')).toHaveLength(0)
  act(() => { vi.advanceTimersByTime(200) })
  expect(screen.queryByTestId('out')).not.toBeInTheDocument()
  view.rerender(<List devices={[output]} />)
  expect(screen.getAllByRole('row')).toHaveLength(1)
})

it('removes outgoing rows immediately with reduced motion and cleans up on unmount', () => {
  vi.useFakeTimers()
  vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: true, addEventListener() {}, removeEventListener() {} } as unknown as MediaQueryList)
  const view = render(<List devices={[output, input]} />)
  view.rerender(<List devices={[input]} />)
  act(() => { vi.advanceTimersByTime(0) })
  expect(screen.queryByTestId('out')).not.toBeInTheDocument()
  view.rerender(<List devices={[]} />)
  view.unmount()
  expect(vi.getTimerCount()).toBe(0)
})
