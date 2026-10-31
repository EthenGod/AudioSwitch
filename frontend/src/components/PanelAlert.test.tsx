import { render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { PanelAlert } from './PanelAlert'

it('commits an initial closed frame and keeps the message while closing without replaying on refresh', () => {
  const frames: (string | undefined)[] = []
  const measure = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this.classList.contains('panel-alert-region')) frames.push(this.dataset.open)
    return {} as DOMRect
  })
  try {
    const view = render(<PanelAlert message="连接失败"><button>重试</button></PanelAlert>)
    const region = view.container.firstElementChild!
    expect(frames).toEqual(['false'])
    expect(region).toHaveAttribute('data-open', 'true')
    const alert = screen.getByRole('alert')
    view.rerender(<PanelAlert message="连接失败"><button>重试</button></PanelAlert>)
    expect(screen.getByRole('alert')).toBe(alert)
    expect(frames).toEqual(['false'])
    view.rerender(<PanelAlert message=""><button>重试</button></PanelAlert>)
    expect(region).toHaveAttribute('data-open', 'false')
    expect(region).toHaveAttribute('inert')
    expect(region).toHaveTextContent('连接失败')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    view.rerender(<PanelAlert message="重试失败"><button>重试</button></PanelAlert>)
    expect(region).toHaveAttribute('data-open', 'true')
    expect(region).not.toHaveAttribute('inert')
    expect(screen.getByRole('alert')).toHaveTextContent('重试失败')
  } finally { measure.mockRestore() }
})
