import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { createDesktopGateway } from './data/desktop'
import { createMockGateway } from './data/mock'
import { backendFixture } from './test/backendFixture'
const running = { Token: '1', Kind: 'files', Status: 'running', Message: '正在检查' }
const passed = { ...running, Status: 'passed', Message: '后台文件检查通过', Directory: 'C:\\隔离目录', Entries: ['通过：AudioSwitch.exe'] }
async function setup(call: (command: string) => Promise<unknown>, title = '文件检查') {
  const read = vi.fn(async () => ({ ...backendFixture(), PanelApiVersion: 3 })), user = userEvent.setup()
  render(<App gateway={createDesktopGateway(read, call)} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  await user.click(screen.getByRole('button', { name: new RegExp(title) }))
  return { user, read, dialog: screen.getByRole('dialog') }
}
describe('maintenance UI', () => {
  it('starts only on click, locks duplicates, and shows actual file results', async () => {
    const call = vi.fn(async (command: string) => command === 'start_maintenance' ? running : passed)
    const { user, dialog, read } = await setup(call)
    expect(call).not.toHaveBeenCalled()
    const start = within(dialog).getByRole('button', { name: '开始检查' }); fireEvent.click(start); fireEvent.click(start)
    fireEvent.focus(window); expect(read).toHaveBeenCalledTimes(1)
    await within(dialog).findByText('后台文件检查通过')
    expect(call.mock.calls.filter(([name]) => name === 'start_maintenance')).toHaveLength(1)
    expect(within(dialog).getByText('通过：AudioSwitch.exe')).toBeVisible()
    expect(within(dialog).queryByRole('button', { name: /修复/ })).not.toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: '关闭' }))
  })
  it('Escape waits for confirmed child exit before closing', async () => {
    let finish!: (value: unknown) => void
    const call = vi.fn(async (command: string) => command === 'cancel_maintenance' ? new Promise(resolve => { finish = resolve }) : running)
    const { user, dialog } = await setup(call)
    await user.click(within(dialog).getByRole('button', { name: '开始检查' })); await user.keyboard('{Escape}')
    expect(dialog).toBeVisible(); expect(within(dialog).getByText('正在取消并等待检查退出…')).toBeVisible()
    finish({ ...running, Status: 'cancelled', Message: '检查已取消' })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(call.mock.calls.filter(([name]) => name === 'cancel_maintenance')).toHaveLength(1)
  })
  it('closing while start is pending cancels the returned token', async () => {
    let finish!: (value: unknown) => void
    const call = vi.fn(async (command: string) => command === 'start_maintenance' ? new Promise(resolve => { finish = resolve }) : { ...running, Status: 'cancelled', Message: '已取消' })
    const { user, dialog } = await setup(call)
    await user.click(within(dialog).getByRole('button', { name: '开始检查' })); await user.click(within(dialog).getByRole('button', { name: '关闭' }))
    finish(running)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(call).toHaveBeenCalledWith('cancel_maintenance', { token: '1' })
  })
  it('stops polling on read failure and lets cancellation be retried', async () => {
    const call = vi.fn(async (command: string) => { if (command === 'start_maintenance') return running; throw { message: '检查连接中断', requiresRefresh: false } })
    const { user, dialog } = await setup(call)
    await user.click(within(dialog).getByRole('button', { name: '开始检查' }))
    await within(dialog).findByRole('alert'); expect(within(dialog).getByRole('button', { name: '重新读取状态' })).toBeEnabled()
    await user.click(within(dialog).getByRole('button', { name: '取消检查' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('检查连接中断')
    expect(within(dialog).getByRole('button', { name: '取消检查' })).toBeEnabled()
  })
  it('displays release notes as text and never offers installation', async () => {
    const result = { Token: '1', Kind: 'update', Status: 'available', Message: '发现新版本', CurrentVersion: '0.11.1', LatestVersion: 'v0.12.0', Notes: '<script>window.bad = true</script>' }
    const { user, dialog } = await setup(async () => result, '检查更新')
    await user.click(within(dialog).getByRole('button', { name: '开始检查' }))
    await within(dialog).findByText('发现新版本'); expect(within(dialog).getByText(result.Notes)).toBeVisible()
    expect(within(dialog).queryByRole('button', { name: /安装|下载/ })).not.toBeInTheDocument()
  })
  it('simulation states explicitly that no local files were checked', async () => {
    const user = userEvent.setup(); render(<App gateway={createMockGateway(0)} />)
    await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' })); await user.click(screen.getByRole('button', { name: /文件检查/ }))
    await user.click(screen.getByRole('button', { name: '模拟检查' }))
    await screen.findByText('模拟检查通过，未读取本机文件。')
  })
})
