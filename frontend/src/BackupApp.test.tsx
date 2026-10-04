import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { createDesktopGateway } from './data/desktop'
import { createMockGateway } from './data/mock'
import { backendFixture } from './test/backendFixture'

const summary = { Token: '1', FileName: '中文备份.json', Devices: 4, Profiles: 3, Rules: 1, DolbyProfiles: 1, OfflineDevices: 1 }
function setup(call: (command: string, args: Record<string, unknown>) => Promise<unknown>) {
  const read = vi.fn(async () => ({ ...backendFixture(), PanelApiVersion: 2 }))
  render(<App gateway={createDesktopGateway(read, call)} />)
  return { read, user: userEvent.setup() }
}
async function open(user: ReturnType<typeof userEvent.setup>, mode = '导入设置') {
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  await user.click(screen.getByRole('button', { name: new RegExp(mode) })); return screen.findByRole('dialog')
}
describe('backup UI', () => {
  it('shows summary without writing, survives focus, and discards on Escape', async () => {
    const call = vi.fn(async () => summary), { user, read } = setup(call)
    const dialog = await open(user); await user.click(within(dialog).getByRole('button', { name: '选择备份文件' }))
    await within(dialog).findByText('中文备份.json'); expect(within(dialog).getByText('4 台（当前离线 1 台）')).toBeVisible()
    fireEvent.focus(window); expect(read).toHaveBeenCalledTimes(1)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(call.mock.calls).toEqual([['choose_import', {}], ['discard_import', { token: '1' }]])
  })
  it('requires explicit confirmation and locks duplicate clicks until actual result', async () => {
    let finish!: (value: unknown) => void
    const call = vi.fn(async (command: string) => command === 'choose_import' ? summary : new Promise(resolve => { finish = resolve }))
    const { user } = setup(call), dialog = await open(user)
    await user.click(within(dialog).getByRole('button', { name: '选择备份文件' })); await within(dialog).findByText('中文备份.json')
    const confirm = within(dialog).getByRole('button', { name: '确认导入' }); fireEvent.click(confirm); fireEvent.click(confirm)
    expect(call.mock.calls.filter(([command]) => command === 'confirm_import')).toHaveLength(1)
    expect(within(dialog).getByRole('button', { name: '取消' })).toBeDisabled()
    const reply = { ...backendFixture(), PanelApiVersion: 2, OperationError: null, PreferencesSaved: true, BackupPath: 'C:\\backups\\原配置.json' }
    reply.Preferences.DarkMode = false; finish(reply)
    await within(dialog).findByText('配置已导入'); expect(within(dialog).getByText(reply.BackupPath)).toBeVisible()
    expect(document.documentElement).not.toHaveClass('dark')
  })
  it('requires refresh after uncertain import and offers no second confirmation', async () => {
    const call = vi.fn(async (command: string) => { if (command === 'choose_import') return summary; throw { message: '结果未确认，请刷新', requiresRefresh: true } })
    const { user } = setup(call), dialog = await open(user)
    await user.click(within(dialog).getByRole('button', { name: '选择备份文件' })); await within(dialog).findByText('中文备份.json')
    await user.click(within(dialog).getByRole('button', { name: '确认导入' })); await within(dialog).findByRole('alert')
    expect(within(dialog).queryByRole('button', { name: '确认导入' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: '选择备份文件' })).not.toBeInTheDocument()
  })
  it('does not show export success for cancellation or disk errors', async () => {
    const call = vi.fn().mockResolvedValueOnce(null).mockRejectedValueOnce({ message: '写入失败：磁盘空间不足', requiresRefresh: false })
    const { user } = setup(call), dialog = await open(user, '导出备份')
    await user.click(within(dialog).getByRole('button', { name: '选择保存位置' })); expect(within(dialog).queryByText('备份已导出')).not.toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: '选择保存位置' })); expect(await within(dialog).findByRole('alert')).toHaveTextContent('磁盘空间不足')
  })
  it('browser simulation never claims to have created a real file', async () => {
    render(<App gateway={createMockGateway(0)} />); const user = userEvent.setup(), dialog = await open(user, '导出备份')
    await user.click(within(dialog).getByRole('button', { name: '模拟导出' })); await within(dialog).findByText('模拟流程已完成')
    expect(within(dialog).getByText('模拟预览：没有生成文件')).toBeVisible()
    expect(within(dialog).queryByText('备份已导出')).not.toBeInTheDocument()
  })
})
