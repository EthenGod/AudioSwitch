import { StrictMode } from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { createDesktopGateway } from './data/desktop'
import { backendFixture } from './test/backendFixture'

function fixture() { return { ...backendFixture(), PanelApiVersion: 1, OperationError: null as string | null } }
const details = { DeviceSettings: { CurrentVolume: 42, VolumeError: null, SpatialError: null,
  Spatial: { Supported: true, CurrentFormat: '', Options: [{ Id: '', Name: '关闭空间音效' }, { Id: 'actual-format', Name: '真实驱动选项' }] } } }
describe('desktop write UI with isolated transport', () => {
  it('uses actual capabilities, survives focus and cancels without a save', async () => {
    const reply = fixture(), call = vi.fn(async () => details), read = vi.fn(async () => reply)
    const user = userEvent.setup()
    render(<StrictMode><App gateway={createDesktopGateway(read, call)} /></StrictMode>)
    await screen.findByRole('table')
    await user.click(screen.getAllByRole('button', { name: /设置 同名扬声器/ }).at(-1)!)
    await screen.findByRole('option', { name: '真实驱动选项' })
    expect(screen.queryByRole('option', { name: /示例/ })).not.toBeInTheDocument()
    await user.click(screen.getByRole('switch', { name: '使用指定音量' }))
    fireEvent.focus(window)
    expect(screen.getByRole('dialog')).toBeVisible()
    expect(screen.getByRole('switch', { name: '使用指定音量' })).toBeChecked()
    await user.click(screen.getByRole('button', { name: '取消' }))
    expect(call).toHaveBeenCalledTimes(1)
    expect(call).toHaveBeenCalledWith('read_device_settings', { id: 'endpoint-a' })
  })
  it('saves offline rules without clearing unreadable fields or overwriting a conflict', async () => {
    const reply = fixture(); reply.OperationError = '此设备预设已被其他窗口修改，请重新打开。'
    const call = vi.fn(async () => reply), user = userEvent.setup()
    render(<App gateway={createDesktopGateway(async () => reply, call)} />)
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: /设置 离线耳机/ }))
    expect(screen.getByLabelText('空间音效预设')).toBeDisabled()
    await user.selectOptions(screen.getByLabelText('白名单规则'), '1')
    await user.click(screen.getByRole('button', { name: '仅保存' }))
    await waitFor(() => expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('其他窗口修改'))
    expect(screen.getByLabelText('白名单规则')).toHaveValue('1')
    expect(call).toHaveBeenCalledWith('panel_action', { action: { kind: 'saveBasic', id: 'offline',
      profile: { Volume: 35, SpatialFormat: '{unknown-format}' }, expectedProfile: { Volume: 35, SpatialFormat: '{unknown-format}' }, rule: 1, expectedRule: 0 } })
  })
  it('retains unreadable live volume and spatial settings while saving a rule', async () => {
    const reply = fixture(), user = userEvent.setup()
    const call = vi.fn(async (command: string) => {
      if (command === 'read_device_settings') throw { message: '读取失败：中文错误', requiresRefresh: false }
      reply.Preferences.DeviceRules['endpoint-b'] = 1
      return reply
    })
    render(<App gateway={createDesktopGateway(async () => reply, call)} />)
    await screen.findByRole('table')
    await user.click(screen.getAllByRole('button', { name: /设置 同名扬声器/ })[0])
    await screen.findByText(/读取失败：中文错误/)
    expect(screen.getByRole('switch', { name: '使用指定音量' })).toHaveAttribute('aria-disabled', 'true')
    await user.selectOptions(screen.getByLabelText('白名单规则'), '1')
    await user.click(screen.getByRole('button', { name: '仅保存' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(call).toHaveBeenLastCalledWith('panel_action', { action: { kind: 'saveBasic', id: 'endpoint-b',
      profile: { Volume: 0, SpatialFormat: '' }, expectedProfile: { Volume: 0, SpatialFormat: '' }, rule: 1, expectedRule: 2 } })
  })
  it('keeps the real checkbox unchanged on failure and blocks timeout repeats until refresh', async () => {
    const reply = fixture(), call = vi.fn().mockRejectedValue({ message: '操作超时，请先刷新确认。', requiresRefresh: true }), user = userEvent.setup()
    render(<App gateway={createDesktopGateway(async () => reply, call)} />)
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: '自动切换' }))
    await user.click(screen.getByRole('switch', { name: '按设备优先级选择' }))
    await screen.findByText('操作超时，请先刷新确认。')
    expect(screen.getByRole('switch', { name: '按设备优先级选择' })).toBeChecked()
    expect(screen.getByRole('switch', { name: '按设备优先级选择' })).toHaveAttribute('aria-disabled', 'true')
    await user.click(screen.getByRole('button', { name: '声音设备' }))
    await user.click(screen.getByRole('button', { name: '刷新状态' }))
    await screen.findByRole('table')
    expect(screen.getByText('已连接真实后台，操作会改变系统设置')).toBeVisible()
    expect(call).toHaveBeenCalledTimes(1)
  })
})

