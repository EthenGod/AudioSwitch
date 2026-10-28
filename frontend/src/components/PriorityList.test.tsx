import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { PriorityList } from './PriorityList'
import { createMockGateway } from '@/data/mock'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function setup() {
  vi.stubGlobal('PointerEvent', MouseEvent)
  const snapshot = await createMockGateway(0).read()
  let finish!: (okay: boolean) => void
  const save = vi.fn(() => new Promise<boolean>(resolve => { finish = resolve }))
  const props = { order: snapshot.Preferences.DeviceOrder[0], devices: snapshot.Devices, busy: false, readOnly: false, label: '优先级', onReorder: save }
  const view = render(<PriorityList {...props} />)
  const rows = [...view.container.querySelectorAll<HTMLElement>('.priority-row')]
  rows.forEach((row, index) => vi.spyOn(row, 'getBoundingClientRect').mockReturnValue({ top: index * 56, height: 56 } as DOMRect))
  const handles = screen.getAllByRole('button', { name: /^拖动/ })
  handles.forEach(handle => { handle.setPointerCapture = vi.fn(); handle.releasePointerCapture = vi.fn(); handle.hasPointerCapture = () => true })
  return { ...view, props, rows, handles, save, finish: (okay: boolean) => finish(okay) }
}
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
