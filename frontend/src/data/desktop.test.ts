import { describe, expect, it, vi } from 'vitest'
import { createDesktopGateway, mapSnapshot } from './desktop'
import { backendFixture } from '../test/backendFixture'

describe('read-only backend boundary', () => {
  it('preserves IDs, independent roles, offline order and null/zero/off distinctions', () => {
    const result = mapSnapshot(backendFixture())
    expect(result.Devices.map(d => [d.Id, d.Name, d.Online])).toEqual([
      ['offline', '离线耳机', false], ['endpoint-b', '同名扬声器', true], ['endpoint-a', '同名扬声器', true], ['mic', '中文麦克风', true],
    ])
    expect(result.Preferences.DeviceOrder).toEqual({ 0: ['offline', 'endpoint-b', 'endpoint-a'], 1: ['mic'] })
    expect(result.Defaults['0:1']).toBe('endpoint-a'); expect(result.Defaults['0:2']).toBe('endpoint-b')
    expect(result.Preferences.DeviceProfiles['endpoint-a']).toEqual({ Volume: null, SpatialFormat: null })
    expect(result.Preferences.DeviceProfiles['endpoint-b']).toEqual({ Volume: 0, SpatialFormat: '' })
    expect(result.Preferences.DeviceProfiles.offline.SpatialFormat).toBe('{unknown-format}')
    expect(result.Preferences.DeviceRules['endpoint-a']).toBeUndefined()
  })
  it('supports empty state and displays backend warnings without interpreting them as disconnection', () => {
    const data = backendFixture(); data.State.Devices = []; data.State.Defaults = {} as typeof data.State.Defaults
    data.Warning = '设备预设未能应用'; data.Error = '设备读取暂时失败'
    const result = mapSnapshot(data)
    expect(result.Devices.every(d => !d.Online)).toBe(true)
    expect(result.BackendWarning).toBe('设备读取暂时失败；设备预设未能应用')
  })
  it('rejects malformed state rather than treating missing fields as false or null', () => {
    const data = backendFixture()
    expect(() => mapSnapshot({ Error: '声间正在退出' })).toThrow('声间正在退出')
    expect(() => mapSnapshot({ ...data, Preferences: { ...data.Preferences, DarkMode: 'false' } })).toThrow('不完整')
    data.State.Devices.push(data.State.Devices[0])
    expect(() => mapSnapshot(data)).toThrow('不完整')
    const badVolume = backendFixture(); badVolume.Preferences.DeviceProfiles['endpoint-b'].Volume = 101
    expect(() => mapSnapshot(badVolume)).toThrow('不完整')
  })
  it('merges concurrent reads, propagates Chinese transport errors and can reconnect', async () => {
    const read = vi.fn().mockRejectedValueOnce('后台连接已中断：中文错误').mockResolvedValue(backendFixture())
    const gateway = createDesktopGateway(read)
    const first = gateway.read(), second = gateway.read()
    expect(first).toBe(second)
    await expect(first).rejects.toThrow('中文错误')
    await expect(gateway.read()).resolves.toHaveProperty('Devices')
    expect(read).toHaveBeenCalledTimes(2)
  })
  it('rejects every mutating method without making a native call', async () => {
    const read = vi.fn(), gateway = createDesktopGateway(read)
    await expect(gateway.switchDevice('endpoint-a')).rejects.toThrow('只读')
    await expect(gateway.saveDevice('endpoint-a', { Volume: null, SpatialFormat: null }, 0)).rejects.toThrow('只读')
    await expect(gateway.setPreference('DarkMode', false)).rejects.toThrow('只读')
    await expect(gateway.setStartup(true)).rejects.toThrow('只读')
    await expect(gateway.reorder(0, [])).rejects.toThrow('只读')
    expect(read).not.toHaveBeenCalled()
  })
})
