import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { Toast } from './Toast'

it('keeps content through exit, removes interaction immediately, and reverses when a new notice arrives', () => {
  const close = vi.fn()
  const view = render(<Toast notice={null} onClose={close} />)
  const element = view.container.firstElementChild!
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
  view.rerender(<Toast notice={{ text: '预设已保存在本次预览中。' }} onClose={close} />)
  expect(screen.getByRole('status')).toHaveTextContent(/^预设已保存在本次预览中$/)
  expect(element).toHaveAttribute('data-open', 'true')
  fireEvent.click(screen.getByRole('button', { name: '关闭操作提示' }))
  expect(close).toHaveBeenCalledOnce()
  view.rerender(<Toast notice={null} onClose={close} />)
  expect(element).toHaveAttribute('data-open', 'false')
  expect(element).toHaveAttribute('inert')
  expect(element).toHaveTextContent('预设已保存在本次预览中')
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
  view.rerender(<Toast notice={{ text: '保存失败。请重试。', error: true }} onClose={close} />)
  expect(screen.getByRole('alert')).toBe(element)
  expect(element).toHaveTextContent(/^保存失败。请重试$/)
  expect(element).not.toHaveAttribute('inert')
})

it('commits a closed initial frame even when mounted with a message', () => {
  const frames: (string | undefined)[] = []
  const measure = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this.classList.contains('toast')) frames.push(this.dataset.open)
    return {} as DOMRect
  })
  try {
    render(<Toast notice={{ text: '已保存' }} onClose={() => {}} />)
    expect(frames).toEqual(['false'])
    expect(screen.getByRole('status')).toHaveAttribute('data-open', 'true')
  } finally { measure.mockRestore() }
})
