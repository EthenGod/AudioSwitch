// IDs, roles and nullable profile semantics mirror src/Models.cs.
import type { DolbyOperation, DolbyProfile, DolbyRead } from './dolby'
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
  DolbyProfiles?: Record<string, DolbyProfile | null>
  CanEditDolby?: boolean
  CanApplyDolby?: boolean
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
  CanCheckMaintenance?: boolean
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
export type MaintenanceKind = 'update' | 'files'
export interface MaintenanceJob {
  Token: string; Kind: MaintenanceKind
  Status: 'running' | 'available' | 'current' | 'ahead' | 'unavailable' | 'passed' | 'failed' | 'cancelled' | 'error'
  Message: string; CurrentVersion?: string; LatestVersion?: string; Notes?: string; Directory?: string; Entries?: string[]
}
/** The desktop implementation permits only explicit panel commands. */
export interface AudioGateway {
  startDolbyApply(id: string, profile: DolbyProfile, expected: DolbyProfile | null, token: string): Promise<{ snapshot: Snapshot; operation: DolbyOperation }>
  readDolbyApply(token: string): Promise<DolbyOperation>
  cancelDolbyApply(token: string): Promise<DolbyOperation>
  saveDolby(id: string, profile: DolbyProfile | null, expected: DolbyProfile | null): Promise<Snapshot>
  startDolbyRead(id: string): Promise<DolbyRead>
  readDolby(token: string): Promise<DolbyRead>
  cancelDolbyRead(token: string): Promise<DolbyRead>
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
  startMaintenance(kind: MaintenanceKind): Promise<MaintenanceJob>
  readMaintenance(token: string): Promise<MaintenanceJob>
  cancelMaintenance(token: string): Promise<MaintenanceJob>
}
export interface PreviewGateway extends AudioGateway {
  readonly mode: 'preview'
  setScenario(scenario: Scenario): Promise<Snapshot>
}
export interface DesktopGateway extends AudioGateway { readonly mode: 'desktop' }
export type UiGateway = PreviewGateway | DesktopGateway
