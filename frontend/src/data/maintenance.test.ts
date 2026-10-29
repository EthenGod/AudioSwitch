import { describe, expect, it, vi } from 'vitest'
import { createDesktopGateway, mapMaintenance } from './desktop'
import { backendFixture } from '../test/backendFixture'

describe('maintenance transport', () => {
  it('blocks unsupported backends before starting a process', async () => {
    const call = vi.fn(), gateway = createDesktopGateway(async () => ({ ...backendFixture(), PanelApiVersion: 2 }), call)
    await gateway.read(); await expect(gateway.startMaintenance('files')).rejects.toThrow('新版后台')
    expect(call).not.toHaveBeenCalled()
  })
  it('allows only kind and opaque token, without executable paths or URLs', async () => {
    const running = { Token: '11', Kind: 'files', Status: 'running', Message: '正在检查' }
    const call = vi.fn(async (command: string) => command === 'cancel_maintenance' ? { ...running, Status: 'cancelled' } : running)
    const gateway = createDesktopGateway(async () => ({ ...backendFixture(), PanelApiVersion: 3 }), call); await gateway.read()
    await gateway.startMaintenance('files'); await gateway.readMaintenance('11'); await gateway.cancelMaintenance('11')
    expect(call.mock.calls).toEqual([['start_maintenance', { kind: 'files' }], ['read_maintenance', { token: '11' }], ['cancel_maintenance', { token: '11' }]])
  })
  it('rejects wrong tokens, unfinished cancellation and incomplete success', async () => {
    const reply = { Token: '2', Kind: 'files', Status: 'running', Message: '正在检查' }
    const gateway = createDesktopGateway(async () => ({ ...backendFixture(), PanelApiVersion: 3 }), async () => reply); await gateway.read()
    await expect(gateway.readMaintenance('1')).rejects.toThrow('不完整')
    await expect(gateway.cancelMaintenance('2')).rejects.toThrow('尚未确认')
    expect(() => mapMaintenance({ ...reply, Status: 'passed' })).toThrow('不完整')
    expect(() => mapMaintenance({ ...reply, Status: 'installed' })).toThrow('不完整')
  })
  it('merges no duplicate start and does not mark read-only failure as uncertain audio write', async () => {
    let reject!: (reason: unknown) => void
    const call = vi.fn(() => new Promise((_, fail) => { reject = fail })), gateway = createDesktopGateway(async () => ({ ...backendFixture(), PanelApiVersion: 3 }), call)
    await gateway.read(); const task = gateway.startMaintenance('update')
    await expect(gateway.startMaintenance('update')).rejects.toThrow('上一个请求')
    reject({ message: '进程启动失败', requiresRefresh: false })
    await expect(task).rejects.toMatchObject({ requiresRefresh: false })
    expect(call).toHaveBeenCalledTimes(1)
  })
})
