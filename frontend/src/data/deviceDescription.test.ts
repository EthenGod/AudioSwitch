import { afterEach, expect, it } from 'vitest'
import { setLanguage } from '../i18n'
import { createMockGateway } from './mock'
import { deviceDescription } from './deviceDescription'
import { mapSnapshot } from './desktop'

afterEach(() => setLanguage('zh-CN'))

it('translates existing sample descriptions immediately without translating device names or aliases', async () => {
  const snapshot = await createMockGateway(0).read()
  const device = snapshot.Devices[0], name = device.Name
  expect(deviceDescription(device)).toBe('USB Audio / 耳机')
  setLanguage('en')
  expect(deviceDescription(device)).toBe('USB Audio / Headphones')
  setLanguage('ja')
  expect(deviceDescription(device)).toBe('USB Audio / ヘッドホン')
  expect(device.Name).toBe(name)
  expect(deviceDescription({ ...device, ConnectionType: undefined, Connection: '个人工作室 / 耳机' })).toBe('个人工作室 / 耳机')
})

it('localizes live endpoint descriptions after switching language without reading another snapshot', () => {
  const snapshot = mapSnapshot({
    State: { Devices: [{ Id: 'output', Name: '我的扬声器', Flow: 0 }], Defaults: {} },
    Preferences: { DeviceOrder: [], DeviceProfiles: {}, DeviceRules: {}, DarkMode: true,
      GameMode: false, AskOnConnect: true, IncludeCommunications: true, UseDevicePriority: true, AutoUpdateEnabled: true },
    Startup: { Enabled: false, Available: false },
  })
  setLanguage('en')
  expect(deviceDescription(snapshot.Devices[0])).toBe('Audio output')
  setLanguage('ja')
  expect(deviceDescription(snapshot.Devices[0])).toBe('音声出力')
  expect(snapshot.Devices[0].Name).toBe('我的扬声器')
})
