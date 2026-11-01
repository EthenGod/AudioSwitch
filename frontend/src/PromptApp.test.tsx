import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
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
  it('keeps the alternate selector secondary and previews actions without writing', async () => {
    const raw = fixture(); raw.State.Devices[0].Name = '桌面扬声器'; raw.State.Devices[1].Name = '新耳机'; raw.Pending[0].PreviousName = '桌面扬声器'
    const { call, gateway: g } = gateway(raw); const { container } = render(<PromptApp gateway={g} />)
    await screen.findByRole('heading', { name: '发现新的输出设备' })
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '保持当前选择' })).not.toBeInTheDocument()
    expect(screen.queryByText('同步通话')).not.toBeInTheDocument()
    const outcome = () => container.querySelector('.prompt-current[data-active=true]')!
    expect(outcome()).toHaveTextContent('当前使用桌面扬声器')
    const use = screen.getByRole('button', { name: '使用新设备' }), previous = screen.getByRole('button', { name: '保留原设备' })
    fireEvent.mouseEnter(use); expect(outcome()).toHaveTextContent('将使用新耳机')
    fireEvent.mouseLeave(use); expect(outcome()).toHaveTextContent('当前使用桌面扬声器')
    fireEvent.focus(previous); expect(outcome()).toHaveTextContent('将恢复桌面扬声器')
    fireEvent.blur(previous); expect(outcome()).toHaveTextContent('当前使用桌面扬声器')
    const close = screen.getByRole('button', { name: '关闭提示' })
    fireEvent.mouseEnter(close); expect(outcome()).toHaveTextContent('将保持桌面扬声器')
    fireEvent.mouseLeave(close); expect(outcome()).toHaveTextContent('当前使用桌面扬声器')
    fireEvent.focus(close); expect(outcome()).toHaveTextContent('将保持桌面扬声器')
    fireEvent.blur(close); expect(outcome()).toHaveTextContent('当前使用桌面扬声器')
    expect(call).not.toHaveBeenCalledWith('close_prompt')
    expect(call.mock.calls.some(([command]) => command === 'prompt_action')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: '选择其他设备' }))
    expect(screen.getByRole('combobox')).toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveAttribute('data-choosing', 'true')
    expect(call).toHaveBeenCalledWith('resize_prompt', { expanded: true, reducedMotion: false })
    fireEvent.click(screen.getByRole('button', { name: '选择其他设备' }))
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveAttribute('data-choosing', 'false')
    expect(call).toHaveBeenCalledWith('resize_prompt', { expanded: false, reducedMotion: false })
  })
  it('keeps the card during exit and sends only one close after the fade', async () => {
    const { call, gateway: g } = gateway(); render(<PromptApp gateway={g} />)
    await screen.findByRole('heading', { name: '发现新的输出设备' })
    await waitFor(() => expect(screen.getByRole('main')).toHaveAttribute('data-phase', 'open'))
    const close = screen.getByRole('button', { name: '关闭提示' })
    fireEvent.click(close); fireEvent.click(close)
    expect(screen.getByRole('main')).toHaveAttribute('data-phase', 'closing')
    expect(screen.getByRole('heading', { name: '发现新的输出设备' })).toBeInTheDocument()
    expect(call).not.toHaveBeenCalledWith('close_prompt')
    fireEvent.click(screen.getByRole('button', { name: '使用新设备' }))
    await screen.findByText('提示已关闭')
    expect(call.mock.calls.filter(([c]) => c === 'close_prompt')).toHaveLength(1)
    expect(call.mock.calls.some(([c]) => c === 'prompt_action')).toBe(false)
    const reads = call.mock.calls.filter(([c]) => c === 'read_prompt_snapshot').length
    await act(() => new Promise(resolve => setTimeout(resolve, 340)))
    expect(call.mock.calls.filter(([c]) => c === 'read_prompt_snapshot')).toHaveLength(reads)
  })
  it('failed panel launch restores the card and allows another attempt', async () => {
    const state = mapPrompt(fixture()), openPanel = vi.fn().mockRejectedValueOnce(new Error('failed')).mockResolvedValue(undefined)
    render(<PromptApp gateway={{ mode: 'preview', read: async () => state, respond: async () => state, close: vi.fn(), openPanel }} />)
    await screen.findByRole('heading', { name: '发现新的输出设备' })
    fireEvent.click(screen.getByRole('button', { name: '打开面板' }))
    expect(openPanel).not.toHaveBeenCalled()
    expect(await screen.findByRole('alert')).toHaveTextContent('无法打开面板，请重试')
    expect(screen.getByRole('main')).toHaveAttribute('data-phase', 'open')
    fireEvent.click(screen.getByRole('button', { name: '打开面板' }))
    await screen.findByText('提示已关闭'); expect(openPanel).toHaveBeenCalledTimes(2)
  })
  it('same-token polling preserves the card and selection, including game mode', async () => {
    const raw = fixture(); raw.Preferences.GameMode = true
    const { call, gateway: g } = gateway(raw); render(<PromptApp gateway={g} />)
    await screen.findByRole('heading', { name: '发现新的输出设备' }); fireEvent.click(screen.getByRole('button', { name: '选择其他设备' }));
    const select = await screen.findByRole('combobox'), card = screen.getByRole('heading').closest('section')
    const user = userEvent.setup(); select.focus(); await user.keyboard('{ArrowDown}')
    await user.click(screen.getByRole('option', { name: '同名扬声器（2）' }))
    const before = call.mock.calls.filter(([c]) => c === 'read_prompt_snapshot').length
    await waitFor(() => expect(call.mock.calls.filter(([c]) => c === 'read_prompt_snapshot').length).toBeGreaterThan(before), { timeout: 650 })
    expect(screen.getByRole('combobox')).toHaveTextContent('同名扬声器（2）')
    expect(screen.getByRole('heading').closest('section')).toBe(card)
    expect(card).not.toHaveAttribute('data-advance')
  })
  it('reduced motion skips the exit delay', async () => {
    const media = vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ matches: query.includes('prefers-reduced-motion'), media: query, onchange: null, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent: () => true }))
    try {
      const { call, gateway: g } = gateway(); render(<PromptApp gateway={g} />)
      await screen.findByRole('heading', { name: '发现新的输出设备' })
      fireEvent.click(screen.getByRole('button', { name: '关闭提示' }))
      expect(call).toHaveBeenCalledWith('close_prompt')
      await screen.findByText('提示已关闭')
    } finally { media.mockRestore() }
  })
  it('native read failures display readable messages and allow retry', async () => {
    const call = vi.fn(async () => { throw { message: '请先打开声间后台。', requiresRefresh: false } })
    render(<PromptApp gateway={createPromptGateway(call)} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('请先打开声间后台。')
    expect(screen.getByRole('heading')).toHaveTextContent('无法读取设备提示')
    expect(screen.getByRole('button', { name: '刷新状态' })).toBeEnabled()
  })
  it('same names use IDs, offline and opposite flow are excluded', async () => {
    const { call, gateway: g } = gateway(); render(<PromptApp gateway={g} />)
    await screen.findByRole('heading', { name: '发现新的输出设备' }); fireEvent.click(screen.getByRole('button', { name: '选择其他设备' }));
    const select = await screen.findByRole('combobox'); expect(select).toHaveTextContent('同名扬声器（1）')
    const user = userEvent.setup(); select.focus(); await user.keyboard('{ArrowDown}')
    expect(screen.getAllByRole('option')).toHaveLength(2)
    await user.click(screen.getByRole('option', { name: '同名扬声器（2）' })); fireEvent.click(screen.getByRole('button', { name: '切换' }))
    await waitFor(() => expect(call).toHaveBeenCalledWith('prompt_action', { action: { kind: 'selected', token: 'output', id: 'endpoint-a' } }))
    await waitFor(() => expect(call).toHaveBeenCalledWith('close_prompt'))
  })
  it('close does not dismiss the pending prompt', async () => {
    const { call, gateway: g } = gateway(); render(<PromptApp gateway={g} />); await screen.findByRole('heading', { name: '发现新的输出设备' })
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
    render(<PromptApp gateway={createPromptGateway(call)} />); await screen.findByRole('heading', { name: '发现新的输出设备' })
    const keep = screen.getByRole('button', { name: '保留原设备' }); fireEvent.click(keep); fireEvent.click(keep)
    expect(call.mock.calls.filter(([c]) => c === 'prompt_action')).toHaveLength(1)
    expect(screen.getByRole('button', { name: '关闭提示' })).toHaveAttribute('aria-disabled', 'true')
    fireEvent.click(screen.getByRole('button', { name: '关闭提示' }))
    expect(call.mock.calls.some(([c]) => c === 'close_prompt')).toBe(false)
    finish({ ...raw, Pending: [{ ...raw.Pending[0], Token: 'input', Flow: 1, NewDevices: [{ Id: 'mic', Flow: 1 }], PreviousDefaults: {} }] })
    await screen.findByRole('heading', { name: '发现新的麦克风' }); expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  })
  it('uncertain response stops polling and blocks another write until manual refresh', async () => {
    const raw = fixture(), call = vi.fn(async (command: string) => { if (command === 'prompt_action') throw new Error('回复丢失'); return raw })
    render(<PromptApp gateway={createPromptGateway(call)} />); await screen.findByRole('heading', { name: '发现新的输出设备' })
    fireEvent.click(screen.getByRole('button', { name: '保留原设备' })); await screen.findByRole('alert')
    expect(screen.getByRole('button', { name: '保留原设备' })).toBeDisabled()
    const reads = call.mock.calls.length
    await new Promise(resolve => setTimeout(resolve, 380)); expect(call.mock.calls).toHaveLength(reads)
    fireEvent.click(screen.getByRole('button', { name: '刷新状态' })); await waitFor(() => expect(screen.getByRole('button', { name: '保留原设备' })).toBeEnabled())
  })
  it('actual failure uses the new snapshot and never reports success', async () => {
    const raw = fixture(), call = vi.fn(async (command: string) => command === 'prompt_action' ? { ...raw, OperationError: '设备已断开' } : raw)
    render(<PromptApp gateway={createPromptGateway(call)} />); await screen.findByRole('heading', { name: '发现新的输出设备' })
    fireEvent.click(screen.getByRole('button', { name: '使用新设备' })); expect(await screen.findByRole('alert')).toHaveTextContent('设备已断开')
    expect(call.mock.calls.some(([c]) => c === 'close_prompt')).toBe(false)
  })
  it('disconnection without online devices can be closed without switching', async () => {
    const raw = fixture(); raw.State.Devices = []; raw.State.Defaults = {} as typeof raw.State.Defaults
    raw.Pending[0].DisconnectedDevices = [{ Id: 'lost', Flow: 0 }]
    const { gateway: g } = gateway(raw); render(<PromptApp gateway={g} />)
    await screen.findByRole('heading', { name: '当前设备已断开' }); expect(screen.getByRole('button', { name: '切换' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: '保留原设备' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '关闭提示' })).toBeEnabled()
  })
  it('rejects malformed or duplicate pending entries instead of silently closing', () => {
    const raw = fixture(); expect(() => mapPrompt({ ...raw, Pending: null })).toThrow()
    expect(() => mapPrompt({ ...raw, Pending: [...raw.Pending, ...raw.Pending] })).toThrow()
    expect(() => mapPrompt({ ...raw, Pending: [{ ...raw.Pending[0], NewDevices: [{ Id: 'mic', Flow: 1 }] }] })).toThrow()
  })
  it('keyboard can close without changing devices; preview is explicit', async () => {
    const user = userEvent.setup(), { gateway: g } = gateway()
    const preview: PromptGateway = { ...g, mode: 'preview' }
    render(<PromptApp gateway={preview} />); await screen.findByRole('heading', { name: '发现新的输出设备' })
    expect(screen.getByText('界面预览，操作不会改变系统设置')).toBeVisible()
    screen.getByRole('button', { name: '关闭提示' }).focus(); await user.keyboard('{Enter}')
    await screen.findByText('提示已关闭')
  })
})
