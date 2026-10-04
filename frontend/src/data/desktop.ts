import { invoke } from '@tauri-apps/api/core'
import type { DesktopGateway, Device, DeviceDetails, DeviceProfile, DeviceRule, Flow, Preferences, Snapshot } from './types'

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
    StartupAvailable: startup.Available === true, StartupCommand: startup.RegisteredCommand == null ? null : string(startup.RegisteredCommand),
    CanWrite: typeof reply.PanelApiVersion === 'number' && reply.PanelApiVersion >= 1,
    DolbyApplying: reply.DolbyApplying === true,
    BackendWarning: [error, optionalText(reply.Warning)].filter(Boolean).join('；') }
}

export class OperationFailure extends Error {
  constructor(message: string, public snapshot?: Snapshot, public requiresRefresh = false) { super(message) }
}
function failure(error: unknown, mutation = false): OperationFailure {
  if (error instanceof OperationFailure) return error
  if (error instanceof Error) return new OperationFailure(error.message, undefined, mutation)
  if (error && typeof error === 'object' && 'message' in error) {
    return new OperationFailure(String(error.message), undefined, 'requiresRefresh' in error && error.requiresRefresh === true)
  }
  return new OperationFailure(String(error), undefined, mutation)
}
export function mapDeviceDetails(value: unknown): DeviceDetails {
  const reply = object(value)
  if (reply.OperationError || !reply.DeviceSettings) throw new Error(optionalText(reply.OperationError ?? reply.Error) || '无法读取设备设置，请重试。')
  const details = object(reply.DeviceSettings)
  const volume = details.CurrentVolume
  if (volume !== null && (typeof volume !== 'number' || !Number.isInteger(volume) || volume < 0 || volume > 100)) throw invalid()
  let spatial: DeviceDetails['Spatial'] = null
  if (details.Spatial !== null) {
    const entry = object(details.Spatial)
    spatial = { Supported: boolean(entry.Supported), CurrentFormat: string(entry.CurrentFormat),
      Options: array(entry.Options).map(value => { const item = object(value); return { Id: string(item.Id), Name: string(item.Name) } }) }
  }
  return { CurrentVolume: volume as number | null, VolumeError: optionalText(details.VolumeError), Spatial: spatial, SpatialError: optionalText(details.SpatialError) }
}

export function createDesktopGateway(readSnapshot: () => Promise<unknown> = () => invoke('read_snapshot'),
  call: (command: string, args: Record<string, unknown>) => Promise<unknown> = invoke): DesktopGateway {
  let pending: Promise<Snapshot> | null = null
  let snapshot: Snapshot | null = null
  const detailReads = new Map<string, Promise<DeviceDetails>>()
  let writing = false, uncertain = false
  const mutate = async (action: Record<string, unknown>): Promise<Snapshot> => {
    if (!snapshot?.CanWrite) throw new OperationFailure('当前后台只支持只读，请使用第三阶段后台。')
    if (writing || pending || detailReads.size) throw new OperationFailure('正在处理上一个请求，请勿重复提交。')
    if (uncertain) throw new OperationFailure('上次操作结果尚未确认，请先刷新状态。', undefined, true)
    writing = true
    try {
      const raw = object(await call('panel_action', { action }))
      const actual = mapSnapshot(raw); snapshot = actual
      if (raw.OperationError) throw new OperationFailure(optionalText(raw.OperationError), actual)
      if (!actual.CanWrite || !Object.hasOwn(raw, 'OperationError')) throw new OperationFailure('后台未确认操作结果，请刷新后检查。', actual, true)
      let matched = true
      if (action.kind === 'preference') matched = actual.Preferences[action.key as keyof Preferences] === action.value
      if (action.kind === 'startup') matched = actual.StartupEnabled === action.value
      if (action.kind === 'switch') {
        const device = actual.Devices.find(d => d.Id === action.id)
        matched = !!device?.Online && (actual.Preferences.IncludeCommunications ? [0, 1, 2] as const : [0, 1] as const)
          .every(role => actual.Defaults[`${device!.Flow}:${role}`] === action.id)
      }
      if (action.kind === 'saveBasic') {
        const saved = actual.Preferences.DeviceProfiles[action.id as string], requested = action.profile as DeviceProfile
        matched = !!saved && saved.Volume === requested.Volume && saved.SpatialFormat === requested.SpatialFormat
          && (actual.Preferences.DeviceRules[action.id as string] ?? 0) === action.rule
      }
      if (action.kind === 'reorder') {
        const order = actual.Preferences.DeviceOrder[action.flow as Flow], requested = action.ids as string[]
        matched = requested.every((id, index) => order[index] === id)
      }
      if (!matched) throw new OperationFailure('后台当前状态与请求不一致，请检查实际结果后再操作。', actual)
      return actual
    } catch (error) {
      const result = failure(error, true); uncertain = result.requiresRefresh; throw result
    } finally { writing = false }
  }
  return {
    mode: 'desktop',
    read() {
      if (writing || detailReads.size) return Promise.reject(new OperationFailure('正在处理操作，请稍后刷新。'))
      // StrictMode and focus events share a single in-flight read.
      if (!pending) pending = Promise.resolve().then(readSnapshot).then(mapSnapshot).then(value => { snapshot = value; uncertain = false; return value })
        .catch(error => { throw failure(error) })
        .finally(() => { pending = null })
      return pending
    },
    readDevice(id) {
      const existing = detailReads.get(id)
      if (existing) return existing
      if (writing || pending || detailReads.size) return Promise.reject(new OperationFailure('正在读取其他状态，请关闭设置后重试。'))
      const request = Promise.resolve().then(() => call('read_device_settings', { id })).then(mapDeviceDetails)
        .catch(error => { throw failure(error) }).finally(() => { detailReads.delete(id) })
      detailReads.set(id, request)
      return request
    },
    switchDevice: id => mutate({ kind: 'switch', id }),
    saveDevice: (id, profile, rule, expectedProfile, expectedRule) => {
      if (!snapshot?.CanWrite) return Promise.reject(new OperationFailure('当前后台只支持只读，请使用第三阶段后台。'))
      if (expectedProfile === undefined || expectedRule === undefined) return Promise.reject(new OperationFailure('缺少原预设，请重新打开设备设置。'))
      return mutate({ kind: 'saveBasic', id, profile, rule, expectedProfile, expectedRule })
    },
    setPreference: (key, value) => mutate({ kind: 'preference', key, value }),
    setStartup: value => mutate({ kind: 'startup', value, expectedCommand: snapshot?.StartupCommand ?? null }),
    reorder: (flow, ids) => mutate({ kind: 'reorder', flow, ids }),
  }
}
