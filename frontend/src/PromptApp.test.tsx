import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { PromptApp } from './PromptApp'
import { canRestore, createPromptGateway, mapPrompt, type PromptGateway } from './data/prompt'
import { backendFixture } from './test/backendFixture'

function fixture() {
  return { ...backendFixture(), PanelApiVersion: 3, OperationError: null as string | null, Pending: [{
    Token: 'output', Flow: 0, NewDevices: [{ Id: 'endpoint-b', Name: '同名扬声器', Flow: 0 }],
    DisconnectedDevices: [] as { Id: string; Flow: number }[], PreviousName: '同名扬声器', PreviousDefaults: { '0:0': 'endpoint-a', '0:1': 'endpoint-a', '0:2': 'offline' },
  }] }
}
function gateway(raw = fixture()) {
  const call = vi.fn(async (command: string) => command === 'read_prompt_snapshot' ? raw : { ...raw, Pending: [] })
  return { call, gateway: createPromptGateway(call) }
}
describe('device prompt', () => {
  it('native read failures display readable messages and allow retry', async () => {
    const call = vi.fn(async () => { throw { message: '请先打开声间后台。', requiresRefresh: false } })
    render(<PromptApp gateway={createPromptGateway(call)} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('请先打开声间后台。')
    expect(screen.getByRole('heading')).toHaveTextContent('无法读取设备提示')
    expect(screen.getByRole('button', { name: '刷新状态' })).toBeEnabled()
  })
  it('same names use IDs, offline and opposite flow are excluded', async () => {
    const { call, gateway: g } = gateway(); render(<PromptApp gateway={g} />)
    const select = await screen.findByRole('combobox'); expect(select).toHaveValue('endpoint-b')
    expect(screen.getAllByRole('option')).toHaveLength(2)
    fireEvent.change(select, { target: { value: 'endpoint-a' } }); fireEvent.click(screen.getByRole('button', { name: '切换' }))
    await waitFor(() => expect(call).toHaveBeenCalledWith('prompt_action', { action: { kind: 'selected', token: 'output', id: 'endpoint-a' } }))
    await waitFor(() => expect(call).toHaveBeenCalledWith('close_prompt'))
  })
  it('close does not dismiss the pending prompt', async () => {
    const { call, gateway: g } = gateway(); render(<PromptApp gateway={g} />); await screen.findByRole('combobox')
    fireEvent.click(screen.getByRole('button', { name: '关闭提示' })); await screen.findByText('提示已关闭')
    expect(call).toHaveBeenCalledWith('close_prompt'); expect(call.mock.calls.some(([c]) => c === 'prompt_action')).toBe(false)
  })
  it('empty pending closes despite historical errors', async () => {
    const raw = fixture(); raw.Pending = []; raw.Error = '之前的 Dolby 失败'
    const { call, gateway: g } = gateway(raw); render(<PromptApp gateway={g} />)
    await waitFor(() => expect(call).toHaveBeenCalledWith('close_prompt'))
  })
  it('previous selection only needs communications when included', () => {
    const raw = fixture(); let state = mapPrompt(raw); expect(canRestore(state, state.pending[0])).toBe(true)
    raw.Preferences.IncludeCommunications = true; state = mapPrompt(raw); expect(canRestore(state, state.pending[0])).toBe(false)
    raw.Preferences.IncludeCommunications = false; raw.Pending[0].DisconnectedDevices.push({ Id: 'lost', Flow: 0 })
    state = mapPrompt(raw); expect(canRestore(state, state.pending[0])).toBe(false)
  })
  it('locks repeated clicks while waiting and advances to another flow', async () => {
    let finish!: (value: unknown) => void
    const raw = fixture(), call = vi.fn(async (command: string) => command === 'prompt_action' ? new Promise(resolve => { finish = resolve }) : raw)
    render(<PromptApp gateway={createPromptGateway(call)} />); await screen.findByRole('combobox')
    const keep = screen.getByRole('button', { name: '保持当前选择' }); fireEvent.click(keep); fireEvent.click(keep)
    expect(call.mock.calls.filter(([c]) => c === 'prompt_action')).toHaveLength(1)
    expect(screen.getByRole('button', { name: '关闭提示' })).toBeDisabled()
    finish({ ...raw, Pending: [{ ...raw.Pending[0], Token: 'input', Flow: 1, NewDevices: [{ Id: 'mic', Flow: 1 }], PreviousDefaults: {} }] })
    await screen.findByRole('heading', { name: '发现新的麦克风' }); expect(screen.getByRole('combobox')).toHaveValue('mic')
  })
  it('uncertain response stops polling and blocks another write until manual refresh', async () => {
    const raw = fixture(), call = vi.fn(async (command: string) => { if (command === 'prompt_action') throw new Error('回复丢失'); return raw })
    render(<PromptApp gateway={createPromptGateway(call)} />); await screen.findByRole('combobox')
    fireEvent.click(screen.getByRole('button', { name: '保持当前选择' })); await screen.findByRole('alert')
    expect(screen.getByRole('button', { name: '保持当前选择' })).toBeDisabled()
    const reads = call.mock.calls.length
    await new Promise(resolve => setTimeout(resolve, 380)); expect(call.mock.calls).toHaveLength(reads)
    fireEvent.click(screen.getByRole('button', { name: '刷新状态' })); await waitFor(() => expect(screen.getByRole('button', { name: '保持当前选择' })).toBeEnabled())
  })
  it('actual failure uses the new snapshot and never reports success', async () => {
    const raw = fixture(), call = vi.fn(async (command: string) => command === 'prompt_action' ? { ...raw, OperationError: '设备已断开' } : raw)
    render(<PromptApp gateway={createPromptGateway(call)} />); await screen.findByRole('combobox')
    fireEvent.click(screen.getByRole('button', { name: '用新设备' })); expect(await screen.findByRole('alert')).toHaveTextContent('设备已断开')
    expect(call.mock.calls.some(([c]) => c === 'close_prompt')).toBe(false)
  })
  it('disconnection without online devices only offers acknowledgment', async () => {
    const raw = fixture(); raw.State.Devices = []; raw.State.Defaults = {} as typeof raw.State.Defaults
    raw.Pending[0].DisconnectedDevices = [{ Id: 'lost', Flow: 0 }]
    const { gateway: g } = gateway(raw); render(<PromptApp gateway={g} />)
    await screen.findByRole('heading', { name: '当前设备已断开' }); expect(screen.getByRole('button', { name: '切换' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: '继续用旧设备' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '知道了' })).toBeEnabled()
  })
  it('rejects malformed or duplicate pending entries instead of silently closing', () => {
    const raw = fixture(); expect(() => mapPrompt({ ...raw, Pending: null })).toThrow()
    expect(() => mapPrompt({ ...raw, Pending: [...raw.Pending, ...raw.Pending] })).toThrow()
    expect(() => mapPrompt({ ...raw, Pending: [{ ...raw.Pending[0], NewDevices: [{ Id: 'mic', Flow: 1 }] }] })).toThrow()
  })
  it('keyboard can select and keep current; preview is explicit', async () => {
    const user = userEvent.setup(), { gateway: g } = gateway()
    const preview: PromptGateway = { ...g, mode: 'preview' }
    render(<PromptApp gateway={preview} />); await screen.findByRole('combobox')
    expect(screen.getByText('界面预览，操作不会改变系统设置')).toBeVisible()
    screen.getByRole('button', { name: '保持当前选择' }).focus(); await user.keyboard('{Enter}')
    await screen.findByText('提示已关闭')
  })
})
