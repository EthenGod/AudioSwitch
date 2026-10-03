import { invoke } from '@tauri-apps/api/core'
import type { DesktopGateway, Device, DeviceProfile, DeviceRule, Flow, Preferences, Snapshot } from './types'

type ObjectValue = Record<string, unknown>
const invalid = () => new Error('后台返回的设备状态不完整，请确认后台版本后重试。')
function object(value: unknown): ObjectValue {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid()
  return value as ObjectValue
}
function string(value: unknown): string { if (typeof value !== 'string') throw invalid(); return value }
function boolean(value: unknown): boolean { if (typeof value !== 'boolean') throw invalid(); return value }
function array(value: unknown): unknown[] { if (!Array.isArray(value)) throw invalid(); return value }
function optionalText(value: unknown): string { return value == null ? '' : string(value) }
function endpoint(value: unknown, online: boolean): Device {
  const row = object(value)
  if (row.Flow !== 0 && row.Flow !== 1) throw invalid()
  const id = string(row.Id)
  if (!id) throw invalid()
  // The existing snapshot does not report connector or form factor. Do not guess USB/Bluetooth.
  return { Id: id, Name: string(row.Name), Flow: row.Flow, Online: online,
    Connection: row.Flow === 0 ? '声音输出' : '麦克风输入',
    Kind: row.Flow === 0 ? 'speaker' : 'microphone' }
}

/** Map src/Models.cs Reply without reading configuration files or changing backend state. */
export function mapSnapshot(value: unknown): Snapshot {
  const reply = object(value)
  const error = optionalText(reply.Error)
  if (!reply.State || !reply.Preferences) throw new Error(error || invalid().message)
  const state = object(reply.State), prefs = object(reply.Preferences)
  const devices = new Map<string, Device>()
  const orders: Record<Flow, string[]> = { 0: [], 1: [] }
  for (const entry of array(prefs.DeviceOrder)) {
    const device = endpoint(entry, false)
    if (devices.has(device.Id)) throw invalid()
    devices.set(device.Id, device); orders[device.Flow].push(device.Id)
  }
  const active = new Set<string>()
  for (const entry of array(state.Devices)) {
    const device = endpoint(entry, true), previous = devices.get(device.Id)
    if (active.has(device.Id) || (previous && previous.Flow !== device.Flow)) throw invalid()
    active.add(device.Id)
    if (!previous) orders[device.Flow].push(device.Id)
    devices.set(device.Id, device)
  }
  const defaults: Snapshot['Defaults'] = {}
  for (const [key, value] of Object.entries(object(state.Defaults))) {
    if (!/^[01]:[012]$/.test(key)) throw invalid()
    if (value === null) continue
    const id = string(value), flow = Number(key[0])
    if (!active.has(id) || devices.get(id)?.Flow !== flow) throw invalid()
    defaults[key as keyof typeof defaults] = id
  }
  const profiles: Record<string, DeviceProfile> = Object.create(null)
  for (const [id, value] of Object.entries(object(prefs.DeviceProfiles))) {
    const profile = object(value), volume = profile.Volume, spatial = profile.SpatialFormat
    if (volume !== null && (typeof volume !== 'number' || !Number.isInteger(volume) || volume < 0 || volume > 100)) throw invalid()
    if (spatial !== null && typeof spatial !== 'string') throw invalid()
    profiles[id] = { Volume: volume as number | null, SpatialFormat: spatial as string | null }
  }
  const rules: Record<string, DeviceRule> = Object.create(null)
  for (const [id, rule] of Object.entries(object(prefs.DeviceRules))) {
    if (rule !== 0 && rule !== 1 && rule !== 2) throw invalid()
    rules[id] = rule
  }
  const preferences: Preferences = {
    DarkMode: boolean(prefs.DarkMode), GameMode: boolean(prefs.GameMode), AskOnConnect: boolean(prefs.AskOnConnect),
    IncludeCommunications: boolean(prefs.IncludeCommunications), UseDevicePriority: boolean(prefs.UseDevicePriority),
    AutoUpdateEnabled: boolean(prefs.AutoUpdateEnabled), DeviceProfiles: profiles, DeviceRules: rules, DeviceOrder: orders,
  }
  const startup = object(reply.Startup)
  return { Devices: [...devices.values()], Defaults: defaults, Preferences: preferences,
    StartupEnabled: boolean(startup.Enabled), StartupMessage: optionalText(startup.Message),
    BackendWarning: [error, optionalText(reply.Warning)].filter(Boolean).join('；') }
}

export function createDesktopGateway(readSnapshot: () => Promise<unknown> = () => invoke('read_snapshot')): DesktopGateway {
  let pending: Promise<Snapshot> | null = null
  const readonly = async (): Promise<Snapshot> => { throw new Error('当前为只读连接，尚未接入真实设置操作。') }
  return {
    mode: 'desktop',
    read() {
      // StrictMode and focus events share a single in-flight read.
      if (!pending) pending = Promise.resolve().then(readSnapshot).then(mapSnapshot)
        .catch(error => { throw error instanceof Error ? error : new Error(String(error)) })
        .finally(() => { pending = null })
      return pending
    },
    switchDevice: readonly, saveDevice: readonly, setPreference: readonly, setStartup: readonly, reorder: readonly,
  }
}
