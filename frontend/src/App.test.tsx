import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { createMockGateway } from './data/mock'

async function setup() {
  const gateway = createMockGateway(0)
  const user = userEvent.setup()
  render(<App gateway={gateway} />)
  await screen.findByRole('table', { name: '音频设备' })
  return { gateway, user }
}

describe('preview workflows', () => {
  it('returns to the top when navigating to another page', async () => {
    const { user } = await setup()
    const main = screen.getByRole('main')
    main.scrollTop = 180
    await user.click(screen.getByRole('button', { name: '应用设置' }))
    expect(main.scrollTop).toBe(0)
  })
  it('filters and searches devices without hiding the preview disclaimer', async () => {
    const { user } = await setup()
    await user.click(screen.getByRole('button', { name: '麦克风输入' }))
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(3)
    await user.type(screen.getByRole('textbox', { name: '搜索设备' }), '不存在')
    expect(screen.getByText('没有找到匹配的设备')).toBeVisible()
    await user.click(screen.getByRole('button', { name: '清除筛选' }))
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(7)
    expect(screen.getByText('界面预览，操作不会改变系统设置')).toBeVisible()
  })
  it('switches only simulated defaults and keeps the result on refresh', async () => {
    const { gateway, user } = await setup()
    await user.click(screen.getByRole('button', { name: /^切换到 桌面扬声器/ }))
    await screen.findByText(/已在预览中切换到/)
    await user.click(screen.getByRole('button', { name: '刷新示例' }))
    await screen.findByRole('table')
    expect((await gateway.read()).Defaults['0:1']).toBe('demo-speakers')
    expect(within(screen.getByRole('region', { name: '当前声音输出' })).getByText('桌面扬声器')).toBeVisible()
  })
  it('cancels a draft, then saves null/off without changing the default device', async () => {
    const { gateway, user } = await setup()
    const open = () => user.click(screen.getByRole('button', { name: /^设置 Studio 耳机/ }))
    await open()
    await user.click(screen.getByRole('switch', { name: '使用指定音量' }))
    await user.click(screen.getByRole('button', { name: '取消' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect((await gateway.read()).Preferences.DeviceProfiles['demo-headphones'].Volume).toBe(65)
    await open()
    await user.click(screen.getByRole('switch', { name: '使用指定音量' }))
    await user.selectOptions(screen.getByLabelText('空间音效预设'), 'off')
    await user.click(screen.getByRole('button', { name: '仅保存（模拟）' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const state = await gateway.read()
    expect(state.Preferences.DeviceProfiles['demo-headphones']).toEqual({ Volume: null, SpatialFormat: '' })
    expect(state.Defaults['0:2']).toBe('demo-speakers')
  })
  it('disables microphone spatial effects and offline device switching', async () => {
    const { user } = await setup()
    expect(screen.getByRole('button', { name: /^切换到 蓝牙耳机/ })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: /^设置 USB 麦克风/ }))
    expect(screen.getByLabelText('空间音效预设')).toBeDisabled()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
  it('supports empty/error scenarios and recovers via retry', async () => {
    const { user } = await setup()
    await user.selectOptions(screen.getByLabelText('预览场景'), 'empty')
    await screen.findByText('还没有声音设备')
    await user.selectOptions(screen.getByLabelText('预览场景'), 'error')
    await screen.findByRole('alert')
    await user.click(screen.getByRole('button', { name: '重试' }))
    await screen.findByRole('table')
  })
  it('reorders offline devices, and preserves order when priority is disabled', async () => {
    const { gateway, user } = await setup()
    await user.click(screen.getByRole('button', { name: '自动切换' }))
    await user.click(screen.getByRole('button', { name: '上移 蓝牙耳机' }))
    await screen.findByText(/已调整示例优先级/)
    await user.click(screen.getByRole('switch', { name: '按设备优先级选择' }))
    await waitFor(() => expect(screen.getByRole('switch', { name: '按设备优先级选择' })).not.toBeChecked())
    expect((await gateway.read()).Preferences.DeviceOrder[0][2]).toBe('demo-bluetooth')
  })
  it('previews appearance/startup and marks unavailable maintenance clearly', async () => {
    const { gateway, user } = await setup()
    await user.click(screen.getByRole('button', { name: '应用设置' }))
    await user.click(screen.getByRole('button', { name: '浅色模式' }))
    await waitFor(() => expect(document.documentElement).not.toHaveClass('dark'))
    await user.click(screen.getByRole('switch', { name: '开机自启' }))
    await waitFor(() => expect(screen.getByRole('switch', { name: '开机自启' })).toBeChecked())
    expect((await gateway.read()).StartupEnabled).toBe(true)
    await user.click(screen.getByRole('button', { name: /Dolby 编辑器/ }))
    expect(screen.getByText(/Dolby 高级编辑器暂未接入/)).toBeVisible()
  })
  it('does not submit a second switch while the first request is pending', async () => {
    const { gateway } = await setup()
    const original = gateway.switchDevice.bind(gateway)
    let release!: () => void
    const pending = new Promise<void>(resolve => { release = resolve })
    const spy = vi.spyOn(gateway, 'switchDevice').mockImplementation(async id => { await pending; return original(id) })
    const target = screen.getByRole('button', { name: /^切换到 桌面扬声器/ })
    fireEvent.click(target); fireEvent.click(target)
    expect(spy).toHaveBeenCalledTimes(1)
    release()
    await screen.findByText(/已在预览中切换到/)
  })
})
