import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { BrowserPort } from './Settings'

afterEach(() => vi.useRealTimers())

it('coalesces typing, rejects invalid drafts, and saves an empty field as automatic', async () => {
  vi.useFakeTimers()
  const save = vi.fn().mockResolvedValue(true)
  const view = render(<BrowserPort value={0} disabled={false} onSave={save} />)
  const input = screen.getByRole('textbox', { name: '端口号' })
  fireEvent.change(input, { target: { value: '4' } })
  await act(() => vi.advanceTimersByTimeAsync(300))
  fireEvent.change(input, { target: { value: '43210' } })
  await act(() => vi.advanceTimersByTimeAsync(600))
  expect(save).toHaveBeenCalledExactlyOnceWith(43210)
  view.rerender(<BrowserPort value={43210} disabled={false} onSave={save} />)
  fireEvent.change(input, { target: { value: '65536' } })
  fireEvent.blur(input)
  await act(() => vi.advanceTimersByTimeAsync(1000))
  expect(save).toHaveBeenCalledTimes(1)
  fireEvent.change(input, { target: { value: '' } })
  await act(() => vi.advanceTimersByTimeAsync(600))
  expect(save).toHaveBeenLastCalledWith(0)
})

it('flushes on blur once and does not repeatedly retry a failed automatic save', async () => {
  vi.useFakeTimers()
  const save = vi.fn().mockResolvedValue(false)
  render(<BrowserPort value={0} disabled={false} onSave={save} />)
  const input = screen.getByRole('textbox', { name: '端口号' })
  fireEvent.change(input, { target: { value: '43210' } })
  await act(async () => { fireEvent.blur(input) })
  await act(() => vi.advanceTimersByTimeAsync(5000))
  expect(save).toHaveBeenCalledExactlyOnceWith(43210)
  expect(input).toHaveValue('43210')
  expect(screen.getByRole('alert')).toHaveTextContent('未保存')
  await act(async () => { fireEvent.keyDown(input, { key: 'Enter' }) })
  expect(save).toHaveBeenCalledTimes(2)
})
