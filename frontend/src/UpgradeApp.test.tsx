import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { createDesktopGateway } from './data/desktop'
import { mapUpgrade } from './data/upgrade'
import { backendFixture } from './test/backendFixture'
import { createMockGateway } from './data/mock'
const available = { Token:'job-1', Kind:'update', Status:'available', CanDownload:true, Bytes:0, Total:4000, Message:'发现可安装版本', Notes:'<script>不可执行说明</script>', Version:'v99.0.0' }
const ready = { ...available, Status:'ready', CanDownload:false, Message:'文件校验通过' }
async function setup(call: (command: string, args: Record<string, unknown>) => Promise<unknown>, title = '检查更新') {
  const user = userEvent.setup()
  render(<App gateway={createDesktopGateway(async () => ({ ...backendFixture(), PanelApiVersion:6 }), call)} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name:'应用设置' })); await user.click(screen.getByRole('button', { name:new RegExp(title) }))
  return { user, dialog:within(screen.getByRole('dialog')) }
}
describe('update and repair confirmation', () => {
  it('saves release scope, renders notes and cancels stale results before changing scope', async () => {
    const gateway = createMockGateway(0), save = vi.spyOn(gateway, 'setPreference'), cancel = vi.spyOn(gateway, 'cancelUpgrade')
    const user = userEvent.setup(); render(<App gateway={gateway} />)
    await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
    await user.click(screen.getByRole('button', { name: /检查更新/ }))
    expect(screen.getByRole('combobox', { name: '更新渠道' })).toHaveTextContent('GitHub')
    await user.click(screen.getByRole('combobox', { name: '接收版本' }))
    await user.click(await screen.findByRole('option', { name: '包含测试版' }))
    await waitFor(() => expect(save).toHaveBeenCalledWith('IncludePrerelease', true))
    await user.click(screen.getByRole('button', { name: '模拟检查' }))
    await screen.findByRole('heading', { name: '✨ 更新内容' }, { timeout: 2500 })
    expect(screen.getByText(/v0.13.0（测试版示例）/)).toBeVisible()
    await user.click(screen.getByRole('combobox', { name: '接收版本' }))
    await user.click(await screen.findByRole('option', { name: '仅正式版' }))
    await waitFor(() => expect(save).toHaveBeenCalledWith('IncludePrerelease', false))
    expect(cancel).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(screen.queryByRole('heading', { name: '✨ 更新内容' })).not.toBeInTheDocument())
  })
  it('requires download and a separate confirmation, never claims installed', async () => {
    const call = vi.fn(async (command: string, args: Record<string, unknown>) => command === 'start_upgrade' ? available : args.action === 'download' ? ready : { ...ready, Status:'handoff', Message:'已交接，尚未确认安装完成' })
    const { user, dialog } = await setup(call)
    expect(call).not.toHaveBeenCalled()
    const start = dialog.getByRole('button', { name:'开始检查' }); fireEvent.click(start); fireEvent.click(start)
    await dialog.findByText('发现可安装版本'); expect(dialog.queryByText('不可执行说明')).not.toBeInTheDocument()
    expect(screen.getByRole('dialog').querySelector('script')).toBeNull()
    expect(call.mock.calls.filter(([c]) => c === 'start_upgrade')).toHaveLength(1)
    expect(dialog.queryByRole('button', { name:'确认安装并重启' })).not.toBeInTheDocument()
    await user.click(dialog.getByRole('button', { name:'下载更新' })); await dialog.findByText('文件校验通过')
    await user.click(dialog.getByRole('button', { name:'准备安装' }))
    expect(dialog.getByText(/启动优先级或 Dolby/)).toBeVisible()
    await user.click(dialog.getByRole('button', { name:'暂不安装' }))
    expect(call.mock.calls.filter(([,a]) => a.action === 'install')).toHaveLength(0)
    await user.click(dialog.getByRole('button', { name:'准备安装' }))
    const button = dialog.getByRole('button', { name:'确认安装并重启' }); fireEvent.click(button); fireEvent.click(button)
    await dialog.findByText('已交接，尚未确认安装完成')
    expect(call.mock.calls.filter(([,a]) => a.action === 'install')).toHaveLength(1)
    expect(dialog.queryByRole('button', { name:'重新检查' })).not.toBeInTheDocument()
  })
  it('keeps the window until download cancellation is confirmed', async () => {
    let finish!: (value: unknown) => void
    const call = vi.fn(async (command: string) => command === 'cancel_upgrade' ? new Promise(resolve => { finish = resolve }) : { ...available, Status:'downloading', CanDownload:false })
    const { user, dialog } = await setup(call)
    await user.click(dialog.getByRole('button', { name:'开始检查' })); await user.keyboard('{Escape}')
    expect(screen.getByRole('dialog')).toBeVisible()
    finish({ ...ready, Status:'cancelled', Message:'任务已停止' })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(call.mock.calls.filter(([c]) => c === 'cancel_upgrade')).toHaveLength(1)
  })
  it('cancels the eventual token when closed during startup', async () => {
    let finish!: (value: unknown) => void
    const call = vi.fn(async (command: string) => command === 'start_upgrade' ? new Promise(resolve => { finish = resolve }) : { ...ready, Status:'cancelled' })
    const { user, dialog } = await setup(call)
    await user.click(dialog.getByRole('button', { name:'开始检查' })); await user.keyboard('{Escape}'); finish(available)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(call).toHaveBeenCalledWith('cancel_upgrade', { token:'job-1' })
  })
  it('does not resubmit an install after a lost response', async () => {
    const call = vi.fn(async (command: string, args: Record<string, unknown>) => {
      if (args.action === 'install') throw new Error('连接中断')
      return command === 'start_upgrade' ? available : ready
    })
    const { user, dialog } = await setup(call)
    await user.click(dialog.getByRole('button', { name:'开始检查' })); await user.click(await dialog.findByRole('button', { name:'下载更新' }))
    await user.click(await dialog.findByRole('button', { name:'准备安装' })); await user.click(dialog.getByRole('button', { name:'确认安装并重启' }))
    expect(await dialog.findByRole('alert')).toHaveTextContent('勿重复提交')
    expect(dialog.queryByRole('button', { name:'确认安装并重启' })).not.toBeInTheDocument()
    expect(dialog.getByRole('button', { name:'重新读取状态' })).toBeEnabled()
    await user.click(dialog.getByRole('button', { name:'重新读取状态' }))
    expect(dialog.queryByRole('button', { name:'准备安装' })).not.toBeInTheDocument()
  })
  it('repair uses the repair kind and candidates never offer download', async () => {
    const call = vi.fn(async () => ({ ...ready, Kind:'repair', Status:'unavailable', Message:'候选包不能安装或修复' }))
    const { user, dialog } = await setup(call, '文件检查')
    await user.click(dialog.getByRole('button', { name:'开始检查' })); await dialog.findByText('候选包不能安装或修复')
    expect(call).toHaveBeenCalledWith('start_upgrade', { kind:'repair' })
    expect(dialog.queryByRole('button', { name:'下载修复包' })).not.toBeInTheDocument()
  })
  it('rejects stale tokens, invented success and invalid progress', () => {
    expect(() => mapUpgrade(available, 'different')).toThrow()
    expect(() => mapUpgrade({ ...available, Status:'installed' })).toThrow()
    expect(() => mapUpgrade({ ...available, Bytes:-1 })).toThrow()
    expect(() => mapUpgrade({ ...ready, CanDownload:true })).toThrow()
    expect(() => mapUpgrade({ ...ready, Entries:[42] })).toThrow()
    expect(() => mapUpgrade({ ...ready, Directory:42 })).toThrow()
    expect(mapUpgrade({ ...ready, Entries:['缺失：面板'], Directory:'示例目录' }).Entries).toEqual(['缺失：面板'])
  })
})
