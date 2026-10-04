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
  BackendWarning?: string
  StartupMessage?: string
  StartupAvailable?: boolean
  StartupCommand?: string | null
  CanWrite?: boolean
  CanManageBackup?: boolean
  DolbyApplying?: boolean
}
export interface DeviceDetails {
  CurrentVolume: number | null
  VolumeError: string
  Spatial: { Supported: boolean; CurrentFormat: string; Options: { Id: string; Name: string }[] } | null
  SpatialError: string
}
export interface ImportPreview {
  Token: string; FileName: string; Devices: number; Profiles: number; Rules: number; DolbyProfiles: number; OfflineDevices: number
}
/** The desktop implementation permits only explicit panel commands. */
export interface AudioGateway {
  readonly mode: 'preview' | 'desktop'
  read(): Promise<Snapshot>
  readDevice(id: string): Promise<DeviceDetails>
  switchDevice(id: string): Promise<Snapshot>
  saveDevice(id: string, profile: DeviceProfile, rule: DeviceRule, expectedProfile?: DeviceProfile | null, expectedRule?: DeviceRule): Promise<Snapshot>
  setPreference(key: PreferenceKey, value: boolean): Promise<Snapshot>
  setStartup(value: boolean): Promise<Snapshot>
  reorder(flow: Flow, ids: string[]): Promise<Snapshot>
  exportBackup(): Promise<{ Path: string } | null>
  chooseImport(): Promise<ImportPreview | null>
  confirmImport(token: string): Promise<{ snapshot: Snapshot; BackupPath: string }>
  discardImport(token: string): Promise<void>
}
export interface PreviewGateway extends AudioGateway {
  readonly mode: 'preview'
  setScenario(scenario: Scenario): Promise<Snapshot>
}
export interface DesktopGateway extends AudioGateway { readonly mode: 'desktop' }
export type UiGateway = PreviewGateway | DesktopGateway
