import type { Device, DeviceProfile, DeviceRule, Flow, MaintenanceJob, PreferenceKey, PreviewGateway, Scenario, Snapshot } from './types'
import { dolbyProfile, emptyDolby, type DolbyRead } from './dolby'

export const SONIC_FORMAT = '{b53d82a5-7b8b-4f03-9c28-12c06378a941}' // Mock identifier only; never sent to Windows.
const devices: Device[] = [
  { Id: 'demo-headphones', Name: 'Studio 耳机', Flow: 0, Online: true, Connection: 'USB Audio · 耳机', Kind: 'headphones' },
  { Id: 'demo-speakers', Name: '桌面扬声器', Flow: 0, Online: true, Connection: 'Realtek Audio · 3.5 mm', Kind: 'speaker' },
  { Id: 'demo-monitor', Name: '显示器音频', Flow: 0, Online: true, Connection: 'DisplayPort · 显示器', Kind: 'monitor' },
  { Id: 'demo-bluetooth', Name: '蓝牙耳机', Flow: 0, Online: false, Connection: 'Bluetooth · 已保留预设', Kind: 'headphones' },
  { Id: 'demo-microphone', Name: 'USB 麦克风', Flow: 1, Online: true, Connection: 'USB Audio · 麦克风', Kind: 'microphone' },
  { Id: 'demo-array', Name: '内置麦克风阵列', Flow: 1, Online: true, Connection: 'Realtek Audio · 内置', Kind: 'microphone' },
]

function initial(): Snapshot {
  return {
    Devices: structuredClone(devices),
    Defaults: { '0:0': 'demo-headphones', '0:1': 'demo-headphones', '0:2': 'demo-speakers', '1:0': 'demo-microphone', '1:1': 'demo-microphone', '1:2': 'demo-microphone' },
    Preferences: {
      DarkMode: true, GameMode: false, AskOnConnect: true, IncludeCommunications: true,
      UseDevicePriority: true, AutoUpdateEnabled: true,
      DeviceProfiles: { 'demo-headphones': { Volume: 65, SpatialFormat: null }, 'demo-bluetooth': { Volume: 45, SpatialFormat: '' } },
      DeviceRules: { 'demo-headphones': 2 },
      DeviceOrder: { 0: devices.filter(d => d.Flow === 0).map(d => d.Id), 1: devices.filter(d => d.Flow === 1).map(d => d.Id) },
    },
    StartupEnabled: false,
  }
}

/** All state lives in this closure. No network, filesystem, storage or native API. */
export function createMockGateway(latency = 260): PreviewGateway {
  let state = initial()
  let dolbyRead: DolbyRead | null = null, dolbySequence = 0, dolbyReady = 0
  let scenario: Scenario = 'normal'
  let importToken = ''
  let importSequence = 0
  let maintenance: MaintenanceJob | null = null, maintenanceSequence = 0, finishAt = 0
  const delay = (ms = latency) => new Promise<void>(resolve => setTimeout(resolve, ms))
  const copy = () => structuredClone(state)
  const device = (id: string) => {
    const found = state.Devices.find(d => d.Id === id)
    if (!found) throw new Error('设备已不在列表中，请刷新后重试。')
    return found
  }
  return {
    mode: 'preview',
    async saveDolby(id, profile, expected) {
      await delay(); if (device(id).Flow !== 0) throw new Error('请选择输出设备。')
      if (JSON.stringify(state.DolbyProfiles?.[id] ?? null) !== JSON.stringify(expected)) throw new Error('方案已变化，请重新打开。')
      state.DolbyProfiles ??= {}; state.DolbyProfiles[id] = dolbyProfile(profile); return copy()
    },
    async startDolbyRead(id) {
      await delay(); if (state.Defaults['0:1'] !== id) throw new Error('请先选择此设备作为当前输出。')
      if (dolbyRead?.Status === 'running') throw new Error('正在读取。')
      dolbyReady = Date.now() + 800; dolbyRead = { Token:`preview-dolby-${++dolbySequence}`, Status:'running', Message:'正在模拟读取…' }; return structuredClone(dolbyRead)
    },
    async readDolby(token) {
      if (!dolbyRead || dolbyRead.Token !== token) throw new Error('读取已失效。')
      if (dolbyRead.Status === 'running' && Date.now() >= dolbyReady) dolbyRead = { Token:token, Status:'ready', Message:'已填入示例方案，未读取实际 Dolby。', Profile:{ ...emptyDolby(), MainProfile:4, SubProfile:4, Enabled:true, Eq:[-27,16,26,56,77,60,43,26,25,24,23,40,56,77,51,21,-22,-31,-42,-66] } }
      return structuredClone(dolbyRead)
    },
    async cancelDolbyRead(token) {
      if (!dolbyRead || dolbyRead.Token !== token) throw new Error('读取已失效。')
      dolbyRead = { Token:token, Status:'cancelled', Message:'模拟读取已取消。' }; return structuredClone(dolbyRead)
    },
    async startMaintenance(kind) {
      if (maintenance?.Status === 'running') throw new Error('正在检查，请先取消。')
      maintenance = { Token: `demo-check-${++maintenanceSequence}`, Kind: kind, Status: 'running', Message: '正在演示检查流程…' }
      finishAt = Date.now() + Math.max(latency * 3, 600)
      return structuredClone(maintenance)
    },
    async readMaintenance(token) {
      if (!maintenance || token !== maintenance.Token) throw new Error('检查已失效。')
      if (maintenance.Status === 'running' && Date.now() >= finishAt) maintenance = maintenance.Kind === 'update'
        ? { ...maintenance, Status: 'available', CurrentVersion: '0.11.1', LatestVersion: 'v0.12.0（示例）', Notes: '示例更新说明：改善界面操作。\n这不是实际发布信息。', Message: '模拟发现新版本，未连接 GitHub。' }
        : { ...maintenance, Status: 'passed', Directory: '示例后台目录（未读取本机文件）', Entries: ['示例通过：AudioSwitch.exe', '示例通过：AudioSwitch.exe.config', '示例通过：vendor/svcl/svcl.exe'], Message: '模拟检查通过，未读取本机文件。' }
      return structuredClone(maintenance)
    },
    async cancelMaintenance(token) {
      if (!maintenance || token !== maintenance.Token) throw new Error('检查已失效。')
      if (maintenance.Status === 'running') maintenance = { ...maintenance, Status: 'cancelled', Message: '模拟检查已取消。' }
      return structuredClone(maintenance)
    },
    async exportBackup() { await delay(); return { Path: '模拟预览：没有生成文件' } },
    async chooseImport() {
      await delay(); importToken = `preview-${++importSequence}`
      return { Token: importToken, FileName: '示例备份.json', Devices: 6, Profiles: 2, Rules: 1, DolbyProfiles: 0, OfflineDevices: 1 }
    },
    async discardImport(token) { if (importToken === token) importToken = '' },
    async confirmImport(token) {
      if (!token || token !== importToken) throw new Error('示例预览已失效，请重新选择。')
      importToken = ''; await delay()
      state.Preferences = initial().Preferences
      return { snapshot: copy(), BackupPath: '模拟预览：没有读写文件或系统配置' }
    },
    async readDevice(id) {
      await delay(); const selected = device(id)
      return { CurrentVolume: selected.Online ? 50 : null, VolumeError: selected.Online ? '' : '设备离线，保留已有预设。',
        Spatial: selected.Flow === 0 ? { Supported: true, CurrentFormat: '', Options: [{ Id: '', Name: '关闭空间音效' }, { Id: SONIC_FORMAT, Name: 'Windows Sonic（示例选项）' }] } : null,
        SpatialError: '' }
    },
    async read() {
      await delay(scenario === 'loading' ? Math.max(latency, 1800) : latency)
      if (scenario === 'error') throw new Error('模拟连接中断。点击重试可恢复示例数据。')
      return copy()
    },
    async setScenario(value) {
      scenario = value
      const dark = state.Preferences.DarkMode
      state = initial()
      state.Preferences.DarkMode = dark
      if (value === 'empty') {
        state.Devices = []; state.Defaults = {}; state.Preferences.DeviceOrder = { 0: [], 1: [] }
      }
      if (value === 'duplicate') {
        state.Devices[1].Name = '扬声器'; state.Devices[2].Name = '扬声器'
      }
      if (value === 'long') state.Devices[0].Name = '用于会议、游戏与音乐制作的高保真桌面音频接口 · 第二代 USB 专业监听耳机（非常长的设备名称示例）'
      return this.read()
    },
    async switchDevice(id) {
      await delay()
      const selected = device(id)
      if (!selected.Online) throw new Error('设备离线，重新连接后才能切换。')
      state.Defaults[`${selected.Flow}:0`] = id
      state.Defaults[`${selected.Flow}:1`] = id
      if (state.Preferences.IncludeCommunications) state.Defaults[`${selected.Flow}:2`] = id
      return copy()
    },
    async saveDevice(id: string, profile: DeviceProfile, rule: DeviceRule) {
      await delay()
      const selected = device(id)
      if (profile.Volume !== null && (!Number.isInteger(profile.Volume) || profile.Volume < 0 || profile.Volume > 100)) throw new Error('音量需要是 0 到 100 的整数。')
      if (![0, 1, 2].includes(rule)) throw new Error('请选择有效的白名单规则。')
      const previous = state.Preferences.DeviceProfiles[id]
      state.Preferences.DeviceProfiles[id] = {
        Volume: profile.Volume,
        SpatialFormat: selected.Flow === 1 ? previous?.SpatialFormat ?? null : profile.SpatialFormat,
      }
      state.Preferences.DeviceRules[id] = rule
      return copy() // Saving never switches a default device.
    },
    async setPreference(key: PreferenceKey, value: boolean) {
      await delay(); state.Preferences[key] = value; return copy()
    },
    async setStartup(value) { await delay(); state.StartupEnabled = value; return copy() },
    async reorder(flow: Flow, ids: string[]) {
      await delay()
      const expected = state.Devices.filter(d => d.Flow === flow).map(d => d.Id)
      if (ids.length !== expected.length || new Set(ids).size !== expected.length || ids.some(id => !expected.includes(id))) throw new Error('排序中的设备不完整，请刷新后重试。')
      state.Preferences.DeviceOrder[flow] = [...ids]
      return copy()
    },
  }
}
