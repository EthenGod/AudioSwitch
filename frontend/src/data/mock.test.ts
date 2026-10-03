import { describe, expect, it } from 'vitest'
import { createMockGateway } from './mock'

describe('preview audio semantics', () => {
  it('preserves communications and input roles when switching output with linking off', async () => {
    const gateway = createMockGateway(0)
    await gateway.setPreference('IncludeCommunications', false)
    const before = await gateway.read()
    const after = await gateway.switchDevice('demo-monitor')
    expect(after.Defaults['0:0']).toBe('demo-monitor')
    expect(after.Defaults['0:1']).toBe('demo-monitor')
    expect(after.Defaults['0:2']).toBe(before.Defaults['0:2'])
    expect(after.Defaults['1:1']).toBe(before.Defaults['1:1'])
  })
  it('keeps save-only separate from routing, and distinguishes null, zero, and off', async () => {
    const gateway = createMockGateway(0)
    const before = await gateway.read()
    const saved = await gateway.saveDevice('demo-monitor', { Volume: 0, SpatialFormat: '' }, 1)
    expect(saved.Defaults).toEqual(before.Defaults)
    expect(saved.Preferences.DeviceProfiles['demo-monitor']).toEqual({ Volume: 0, SpatialFormat: '' })
    const kept = await gateway.saveDevice('demo-monitor', { Volume: null, SpatialFormat: null }, 0)
    expect(kept.Preferences.DeviceProfiles['demo-monitor']).toEqual({ Volume: null, SpatialFormat: null })
  })
  it('does not share profiles between same-name devices', async () => {
    const gateway = createMockGateway(0)
    await gateway.setScenario('duplicate')
    const saved = await gateway.saveDevice('demo-speakers', { Volume: 13, SpatialFormat: '' }, 2)
    expect(saved.Devices.find(d => d.Id === 'demo-speakers')?.Name).toBe(saved.Devices.find(d => d.Id === 'demo-monitor')?.Name)
    expect(saved.Preferences.DeviceProfiles['demo-monitor']).toBeUndefined()
  })
  it('retains offline profiles and rejects offline switching and incomplete order', async () => {
    const gateway = createMockGateway(0)
    await gateway.saveDevice('demo-bluetooth', { Volume: 31, SpatialFormat: null }, 0)
    await expect(gateway.switchDevice('demo-bluetooth')).rejects.toThrow('离线')
    await expect(gateway.reorder(0, ['demo-headphones'])).rejects.toThrow('不完整')
    const state = await gateway.reorder(0, ['demo-bluetooth', 'demo-monitor', 'demo-speakers', 'demo-headphones'])
    expect(state.Preferences.DeviceProfiles['demo-bluetooth'].Volume).toBe(31)
  })
  it('returns isolated snapshots and keeps each preview session independent', async () => {
    const first = createMockGateway(0)
    const external = await first.read()
    external.Preferences.GameMode = true
    expect((await first.read()).Preferences.GameMode).toBe(false)
    await first.setStartup(true)
    expect((await createMockGateway(0).read()).StartupEnabled).toBe(false)
  })
  it('does not invent playback spatial settings for a microphone', async () => {
    const state = await createMockGateway(0).saveDevice('demo-microphone', { Volume: 72, SpatialFormat: 'anything' }, 0)
    expect(state.Preferences.DeviceProfiles['demo-microphone'].SpatialFormat).toBeNull()
  })
})
