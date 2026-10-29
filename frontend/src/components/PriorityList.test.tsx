import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { PriorityList } from './PriorityList'
import { createMockGateway } from '@/data/mock'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function setup(heights = [56, 56, 56, 56]) {
  vi.stubGlobal('PointerEvent', MouseEvent)
  const snapshot = await createMockGateway(0).read()
  let finish!: (okay: boolean) => void
  const save = vi.fn(() => new Promise<boolean>(resolve => { finish = resolve }))
  const props = { order: snapshot.Preferences.DeviceOrder[0], devices: snapshot.Devices, busy: false, readOnly: false, label: '优先级', onReorder: save }
  const view = render(<PriorityList {...props} />)
  const rows = [...view.container.querySelectorAll<HTMLElement>('.priority-row')]
  rows.forEach((row, index) => vi.spyOn(row, 'getBoundingClientRect').mockReturnValue({ top: heights.slice(0, index).reduce((sum, height) => sum + height, 0), height: heights[index] } as DOMRect))
  const handles = screen.getAllByRole('button', { name: /^拖动/ })
  handles.forEach(handle => { handle.setPointerCapture = vi.fn(); handle.releasePointerCapture = vi.fn(); handle.hasPointerCapture = () => true })
  return { ...view, props, rows, handles, save, finish: (okay: boolean) => finish(okay) }
}
it.each([
  [1, 0, 56], [2, 0, 56], [1, 3, 56], [2, 3, 56],
  [1, 0, 55], [2, 0, 55], [1, 3, 55], [2, 3, 55],
])('drags middle row %i to boundary %i when the first row height is %i', async (from, to, firstHeight) => {
  const { handles, save, props, finish } = await setup([firstHeight, 56, 56, 56])
  const start = firstHeight + (from - 1) * 56 + 28
  const edge = to === 0 ? -100 : 400
  fireEvent.pointerDown(handles[from], { button: 0, clientY: start })
  fireEvent.pointerMove(handles[from], { clientY: edge })
  expect(save).not.toHaveBeenCalled()
  fireEvent.pointerUp(handles[from], { clientY: edge })
  const expected = [...props.order]; expected.splice(to, 0, ...expected.splice(from, 1))
  expect(save).toHaveBeenCalledExactlyOnceWith(expected)
  await act(async () => finish(true))
})
it('previews a multi-row pointer drag and submits once on release, restoring the order if saving fails', async () => {
  const { handles, rows, save, props, finish } = await setup()
  fireEvent.pointerDown(handles[0], { button: 0, clientY: 28 })
  fireEvent.pointerMove(handles[0], { clientY: 150 })
  expect(rows[0].style.transform).toBe('translateY(122px)')
  expect(rows[1].style.transform).toBe('translateY(-56px)')
  expect(save).not.toHaveBeenCalled()
  fireEvent.pointerUp(handles[0], { clientY: 150 })
  fireEvent.pointerUp(handles[0], { clientY: 150 })
  expect(save).toHaveBeenCalledExactlyOnceWith([props.order[1], props.order[2], props.order[0], props.order[3]])
  expect(handles[0]).toBeDisabled()
  await act(async () => finish(false))
  expect(rows[0].parentElement?.firstElementChild).toBe(rows[0])
  expect(screen.getByRole('status')).toHaveTextContent('排序未保存')
})
it('cancels on Escape or pointer cancellation and refuses a stale drag after the snapshot changes', async () => {
  const { handles, save, props, rerender } = await setup()
  fireEvent.pointerDown(handles[3], { button: 0, clientY: 196 }); fireEvent.pointerMove(handles[3], { clientY: 20 })
  fireEvent.keyDown(window, { key: 'Escape' }); fireEvent.pointerUp(handles[3])
  fireEvent.pointerDown(handles[0], { button: 0, clientY: 28 }); fireEvent.pointerCancel(handles[0]); fireEvent.pointerUp(handles[0])
  fireEvent.pointerDown(handles[0], { button: 0, clientY: 28 }); fireEvent.pointerMove(handles[0], { clientY: 180 })
  rerender(<PriorityList {...props} order={[...props.order].reverse()} />); fireEvent.pointerUp(handles[0])
  expect(save).not.toHaveBeenCalled()
})
it('supports keyboard reordering and blocks read-only dragging', async () => {
  const { handles, save, props, finish, rerender } = await setup()
  fireEvent.keyDown(handles[0], { key: 'ArrowDown' })
  expect(save).toHaveBeenCalledExactlyOnceWith([props.order[1], props.order[0], ...props.order.slice(2)])
  await act(async () => finish(true))
  rerender(<PriorityList {...props} readOnly />)
  expect(handles[0]).toBeDisabled()
  fireEvent.pointerDown(handles[0], { button: 0, clientY: 28 }); fireEvent.pointerMove(handles[0], { clientY: 180 }); fireEvent.pointerUp(handles[0])
  expect(save).toHaveBeenCalledTimes(1)
})
