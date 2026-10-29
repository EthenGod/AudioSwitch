/** Matches .NET Reply casing and DeviceOrder shape; deliberately same-name endpoints. */
export function backendFixture() {
  return {
    State: {
      Devices: [{ Id: 'endpoint-a', Name: '同名扬声器', Flow: 0 }, { Id: 'endpoint-b', Name: '同名扬声器', Flow: 0 }, { Id: 'mic', Name: '中文麦克风', Flow: 1 }],
      Defaults: { '0:0': 'endpoint-a', '0:1': 'endpoint-a', '0:2': 'endpoint-b', '1:0': 'mic', '1:1': 'mic', '1:2': 'mic' },
    },
    Preferences: {
      DarkMode: true, GameMode: false, AskOnConnect: true, IncludeCommunications: false,
      UseDevicePriority: true, AutoUpdateEnabled: true,
      DeviceOrder: [{ Id: 'offline', Name: '离线耳机', Flow: 0 }, { Id: 'endpoint-b', Name: '旧名称', Flow: 0 }],
      DeviceProfiles: { 'endpoint-a': { Volume: null, SpatialFormat: null }, 'endpoint-b': { Volume: 0, SpatialFormat: '' }, offline: { Volume: 35, SpatialFormat: '{unknown-format}' } },
      DeviceRules: { 'endpoint-b': 2 },
    },
    Startup: { Enabled: false, Message: '尚未设置开机自启' },
    Error: null as string | null, Warning: null as string | null,
  }
}
