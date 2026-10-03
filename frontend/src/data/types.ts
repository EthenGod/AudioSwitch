// IDs, roles and nullable profile semantics mirror src/Models.cs.
// Online/Connection/Kind are preview presentation metadata, not a new wire contract.
export type Flow = 0 | 1
export type Role = 0 | 1 | 2
export type DeviceRule = 0 | 1 | 2
export type Scenario = 'normal' | 'empty' | 'duplicate' | 'long' | 'loading' | 'error'
export type Page = 'devices' | 'automation' | 'settings'
export interface Device {
  Id: string; Name: string; Flow: Flow; Online: boolean
  Connection: string; Kind: 'headphones' | 'speaker' | 'microphone' | 'monitor'
}
export interface DeviceProfile {
  Volume: number | null
  /** null = keep current; empty string = off; otherwise a format ID. */
  SpatialFormat: string | null
}
export interface Preferences {
  DarkMode: boolean; GameMode: boolean; AskOnConnect: boolean
  IncludeCommunications: boolean; UseDevicePriority: boolean; AutoUpdateEnabled: boolean
  DeviceProfiles: Record<string, DeviceProfile>
  DeviceRules: Record<string, DeviceRule>
  DeviceOrder: Record<Flow, string[]>
}
export type PreferenceKey = 'DarkMode' | 'GameMode' | 'AskOnConnect' | 'IncludeCommunications' | 'UseDevicePriority' | 'AutoUpdateEnabled'
export interface Snapshot {
  Devices: Device[]
  Defaults: Partial<Record<`${Flow}:${Role}`, string>>
  Preferences: Preferences
  StartupEnabled: boolean
}
/** The UI only sees this asynchronous boundary. Stage 1 ships MockGateway only. */
export interface AudioGateway {
  read(): Promise<Snapshot>
  switchDevice(id: string): Promise<Snapshot>
  saveDevice(id: string, profile: DeviceProfile, rule: DeviceRule): Promise<Snapshot>
  setPreference(key: PreferenceKey, value: boolean): Promise<Snapshot>
  setStartup(value: boolean): Promise<Snapshot>
  reorder(flow: Flow, ids: string[]): Promise<Snapshot>
}
export interface PreviewGateway extends AudioGateway {
  setScenario(scenario: Scenario): Promise<Snapshot>
}
