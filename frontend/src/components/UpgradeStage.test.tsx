import { act, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { UpgradeJob } from '@/data/types'
import { UpgradeStage } from './UpgradeStage'

const job: UpgradeJob = { Token: 'one', Kind: 'update', Status: 'downloading', Message: '正在下载并校验', CanDownload: false, Bytes: 100, Total: 1000 }
it('shrinks from the current height when a checking reply removes the previous version without changing stage', () => {
  let running = false, visualHeight = 80
  const cancel = vi.fn(() => { running = false })
  const animate = vi.fn(() => { running = true; return { cancel, playState: 'running' } })
  const original = HTMLElement.prototype.animate
  HTMLElement.prototype.animate = animate as unknown as typeof original
  const rect = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    return { height: running ? visualHeight : this.querySelector('.upgrade-status-heading p') ? 80 : 48 } as DOMRect
  })
  try {
    const old = { ...job, Status: 'cancelled' as const, Version: 'v0.13.0', Message: '已取消' }
    const view = render(<UpgradeStage job={old} stage="cancelled" />)
    view.rerender(<UpgradeStage job={old} stage="checking" />)
    visualHeight = 70
    view.rerender(<UpgradeStage job={{ ...job, Token: 'two', Status: 'checking', Message: '正在检查更新' }} stage="checking" />)
    expect(animate).toHaveBeenCalledTimes(2)
    expect(animate.mock.calls[1]).toEqual([
      [expect.objectContaining({ height: '70px' }), expect.objectContaining({ height: '48px' })],
      { duration: 200, easing: 'ease-in' },
    ])
    view.unmount()
  } finally { rect.mockRestore(); HTMLElement.prototype.animate = original }
})
it('animates stage changes, not progress polling, and keeps the same progress element', () => {
  const cancel = vi.fn(), animate = vi.fn(() => ({ cancel, playState: 'running' }))
  const original = HTMLElement.prototype.animate
  HTMLElement.prototype.animate = animate as unknown as typeof original
  try {
    const view = render(<UpgradeStage job={null} stage="idle" />)
    view.rerender(<UpgradeStage job={job} stage="downloading" />)
    const progress = screen.getByRole('progressbar', { name: '下载进度' })
    expect(animate).toHaveBeenCalledTimes(1)
    view.rerender(<UpgradeStage job={{ ...job, Bytes: 300 }} stage="downloading" />)
    expect(screen.getByRole('progressbar')).toBe(progress)
    expect(progress).toHaveAttribute('aria-valuenow', '300')
    expect(animate).toHaveBeenCalledTimes(1)
    view.rerender(<UpgradeStage job={{ ...job, Status: 'ready', Message: '校验通过' }} stage="ready" />)
    expect(animate).toHaveBeenCalledTimes(2); expect(cancel).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
    view.unmount(); expect(cancel).toHaveBeenCalledTimes(2)
  } finally { HTMLElement.prototype.animate = original }
})
it('cancels motion when reduced motion is enabled and does not invent unknown progress', () => {
  let reduced = false, listener = () => {}
  const media = vi.spyOn(window, 'matchMedia').mockImplementation(() => ({ get matches() { return reduced }, addEventListener(_event: string, next: () => void) { listener = next }, removeEventListener() {} } as unknown as MediaQueryList))
  const cancel = vi.fn(), animate = vi.fn(() => ({ cancel, playState: 'running' })), original = HTMLElement.prototype.animate
  HTMLElement.prototype.animate = animate as unknown as typeof original
  try {
    const view = render(<UpgradeStage job={null} stage="idle" />)
    view.rerender(<UpgradeStage job={{ ...job, Total: 0 }} stage="downloading" />)
    expect(screen.getByRole('progressbar')).not.toHaveAttribute('aria-valuenow')
    reduced = true; act(listener); expect(cancel).toHaveBeenCalledTimes(1)
    view.rerender(<UpgradeStage job={{ ...job, Status: 'cancelled' }} stage="cancelled" />)
    expect(animate).toHaveBeenCalledTimes(1)
    view.unmount()
  } finally { HTMLElement.prototype.animate = original; media.mockRestore() }
})
