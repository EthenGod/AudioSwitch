import { describe, expect, it, vi } from 'vitest'
import { createDesktopGateway } from './desktop'
import { backendFixture } from '../test/backendFixture'

export function writableFixture() {
  return { ...backendFixture(), PanelApiVersion: 1, OperationError: null as string | null,
    Startup: { Enabled: false, Available: true, RegisteredCommand: 'original', Message: '' } }
}
describe('desktop operations', () => {
  it('checks actual roles and leaves excluded communications independent', async () => {
    const reply = writableFixture(), call = vi.fn(async () => reply)
    const gateway = createDesktopGateway(async () => reply, call); await gateway.read()
    await expect(gateway.switchDevice('endpoint-b')).rejects.toThrow('不一致')
    reply.State.Defaults['0:0'] = reply.State.Defaults['0:1'] = 'endpoint-b'
    reply.State.Defaults['0:2'] = 'endpoint-a'
    await expect(gateway.switchDevice('endpoint-b')).resolves.toHaveProperty('Defaults')
    expect(call).toHaveBeenLastCalledWith('panel_action', { action: { kind: 'switch', id: 'endpoint-b' } })
  })
  it('sends a narrow save with baseline and distinguishes null/zero/off', async () => {
    const reply = writableFixture(), call = vi.fn(async () => reply)
    const gateway = createDesktopGateway(async () => reply, call); await gateway.read()
    await gateway.saveDevice('endpoint-b', { Volume: 0, SpatialFormat: '' }, 2, { Volume: null, SpatialFormat: null }, 0)
    expect(call).toHaveBeenCalledWith('panel_action', { action: { kind: 'saveBasic', id: 'endpoint-b',
      profile: { Volume: 0, SpatialFormat: '' }, rule: 2, expectedProfile: { Volume: null, SpatialFormat: null }, expectedRule: 0 } })
    await expect(gateway.saveDevice('endpoint-b', { Volume: null, SpatialFormat: null }, 0)).rejects.toThrow('缺少原预设')
    expect(call).toHaveBeenCalledTimes(1)
  })
  it('returns actual saved state on partial failure and ignores historical warnings for success', async () => {
    const reply = writableFixture(); reply.Preferences.UseDevicePriority = false
    reply.OperationError = '设置已保存，但本次应用未完成。'; reply.Error = '历史错误'
    const gateway = createDesktopGateway(async () => reply, async () => reply); await gateway.read()
    await expect(gateway.setPreference('UseDevicePriority', false)).rejects.toMatchObject({ snapshot: { Preferences: { UseDevicePriority: false } }, requiresRefresh: false })
    reply.OperationError = null
    await expect(gateway.setPreference('UseDevicePriority', false)).resolves.toHaveProperty('BackendWarning', '历史错误')
  })
  it('blocks repeat writes until an uncertain result has been refreshed', async () => {
    const reply = writableFixture(), call = vi.fn().mockRejectedValueOnce({ message: '响应超时，结果未确认', requiresRefresh: true }).mockResolvedValue(reply)
    const gateway = createDesktopGateway(async () => reply, call); await gateway.read()
    await expect(gateway.setPreference('DarkMode', true)).rejects.toMatchObject({ requiresRefresh: true })
    await expect(gateway.setPreference('DarkMode', true)).rejects.toThrow('先刷新')
    expect(call).toHaveBeenCalledTimes(1)
    await gateway.read(); await gateway.setPreference('DarkMode', true)
    expect(call).toHaveBeenCalledTimes(2)
  })
  it('serializes mutations and never retries while waiting', async () => {
    const reply = writableFixture(); let release!: (value: unknown) => void
    const call = vi.fn(() => new Promise(resolve => { release = resolve }))
    const gateway = createDesktopGateway(async () => reply, call); await gateway.read()
    const first = gateway.setPreference('DarkMode', true)
    await expect(gateway.setPreference('DarkMode', false)).rejects.toThrow('重复提交')
    await expect(gateway.read()).rejects.toThrow('稍后刷新')
    release(reply); await first; expect(call).toHaveBeenCalledTimes(1)
  })
  it('rejects unsupported startup and preference results instead of optimistic success', async () => {
    const reply = writableFixture(), call = vi.fn(async () => reply)
    const gateway = createDesktopGateway(async () => reply, call); await gateway.read()
    await expect(gateway.setStartup(true)).rejects.toThrow('不一致')
    expect(call).toHaveBeenLastCalledWith('panel_action', { action: { kind: 'startup', value: true, expectedCommand: 'original' } })
    await expect(gateway.setPreference('GameMode', true)).rejects.toThrow('不一致')
  })
  it('merges StrictMode detail reads and keeps capability errors as unavailable fields', async () => {
    const call = vi.fn(async () => ({ DeviceSettings: { CurrentVolume: null, VolumeError: '设备断开', Spatial: null, SpatialError: '不支持' } }))
    const gateway = createDesktopGateway(undefined, call)
    const first = gateway.readDevice('endpoint-a'), second = gateway.readDevice('endpoint-a')
    expect(first).toBe(second)
    await expect(first).resolves.toMatchObject({ CurrentVolume: null, VolumeError: '设备断开', Spatial: null })
    expect(call).toHaveBeenCalledTimes(1)
  })
  it('requires refresh when a backend response lacks a confirmed operation result', async () => {
    const reply = writableFixture()
    const gateway = createDesktopGateway(async () => reply, async () => backendFixture()); await gateway.read()
    await expect(gateway.setPreference('DarkMode', true)).rejects.toMatchObject({ requiresRefresh: true })
  })
})
