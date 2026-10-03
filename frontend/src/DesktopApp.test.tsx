import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { createDesktopGateway } from './data/desktop'
import { backendFixture } from './test/backendFixture'

describe('desktop read-only experience', () => {
  it('disables real operations and permits only local theme changes', async () => {
    const read = vi.fn().mockResolvedValue(backendFixture()), gateway = createDesktopGateway(read)
    const preference = vi.spyOn(gateway, 'setPreference'), switchDevice = vi.spyOn(gateway, 'switchDevice')
    const user = userEvent.setup(); render(<App gateway={gateway} />)
    await screen.findByRole('table')
    expect(screen.getByText('只读连接，仅显示真实状态，不会改变系统设置')).toBeVisible()
    expect(screen.queryByRole('combobox', { name: '预览场景' })).not.toBeInTheDocument()
    const switchButton = screen.getByRole('button', { name: /切换到 同名扬声器/ })
    expect(switchButton).toBeDisabled(); await user.click(switchButton)
    expect(switchDevice).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: /设置 离线耳机/ }))
    const sheet = screen.getByRole('dialog')
    expect(within(sheet).getByRole('switch', { name: '使用指定音量' })).toHaveAttribute('aria-disabled', 'true')
    expect(within(sheet).queryByRole('button', { name: /保存/ })).not.toBeInTheDocument()
    expect(within(sheet).getByRole('combobox', { name: '空间音效预设' })).toHaveValue('{unknown-format}')
    await user.click(within(sheet).getByRole('button', { name: '关闭' }))
    await user.click(screen.getByRole('button', { name: '自动切换' }))
    expect(screen.getByRole('button', { name: '下移 离线耳机' })).toBeDisabled()
    expect(screen.getByRole('switch', { name: '按设备优先级选择' })).toHaveAttribute('aria-disabled', 'true')
    await user.click(screen.getByRole('button', { name: '应用设置' }))
    expect(screen.getByRole('switch', { name: '开机自启' })).toHaveAttribute('aria-disabled', 'true')
    await user.click(screen.getByRole('button', { name: '浅色模式' }))
    expect(document.documentElement).not.toHaveClass('dark')
    expect(preference).not.toHaveBeenCalled()
  })
  it('clears stale devices after disconnect and refreshes on focus without a timer', async () => {
    const read = vi.fn().mockResolvedValueOnce(backendFixture()).mockRejectedValueOnce('后台已退出').mockResolvedValue(backendFixture())
    const user = userEvent.setup(); render(<App gateway={createDesktopGateway(read)} />)
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: '刷新状态' }))
    await screen.findByText('后台已退出')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByText('当前已关闭优先级')).not.toBeInTheDocument()
    fireEvent.focus(window)
    await screen.findByRole('table')
    await waitFor(() => expect(read).toHaveBeenCalledTimes(3))
  })
  it('keeps a dismissed backend warning hidden when the same snapshot is read again', async () => {
    const reply = backendFixture(); reply.Warning = '原预设未能应用'
    const user = userEvent.setup(); render(<App gateway={createDesktopGateway(async () => reply)} />)
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: '关闭后台提示' }))
    await user.click(screen.getByRole('button', { name: '刷新状态' }))
    await screen.findByRole('table')
    expect(screen.queryByText('后台提示：原预设未能应用')).not.toBeInTheDocument()
  })
})
