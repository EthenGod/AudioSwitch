import { describe, expect, it, vi } from 'vitest'
import { createDesktopGateway } from './desktop'
import { backendFixture } from '../test/backendFixture'

export const previewFixture = { Token: '1', FileName: '中文备份.json', Devices: 4, Profiles: 3, Rules: 1, DolbyProfiles: 1, OfflineDevices: 1 }
const ready = () => ({ ...backendFixture(), PanelApiVersion: 2, OperationError: null as string | null, PreferencesSaved: true, BackupPath: 'C:\\example\\before-import.json' })
describe('backup gateway isolated transport', () => {
  it('does not send backup commands to old backends', async () => {
    const call = vi.fn(), gateway = createDesktopGateway(async () => ({ ...ready(), PanelApiVersion: 1 }), call)
    await gateway.read(); await expect(gateway.exportBackup()).rejects.toThrow('新版后台')
    await expect(gateway.chooseImport()).rejects.toThrow('新版后台'); expect(call).not.toHaveBeenCalled()
  })
  it('handles native chooser cancellation without a confirmation or file path request', async () => {
    const call = vi.fn(async () => null), gateway = createDesktopGateway(async () => ready(), call)
    await gateway.read(); expect(await gateway.chooseImport()).toBeNull(); expect(await gateway.exportBackup()).toBeNull()
    expect(call.mock.calls).toEqual([['choose_import', {}], ['export_backup', {}]])
  })
  it('uses opaque tokens and returns actual imported snapshot and original backup path', async () => {
    const reply = ready(); reply.Preferences.DarkMode = false
    const call = vi.fn(async (command: string) => command === 'choose_import' ? previewFixture : reply)
    const gateway = createDesktopGateway(async () => ready(), call); await gateway.read()
    const preview = await gateway.chooseImport(); const result = await gateway.confirmImport(preview!.Token)
    expect(result.snapshot.Preferences.DarkMode).toBe(false); expect(result.BackupPath).toBe(reply.BackupPath)
    expect(call).toHaveBeenLastCalledWith('confirm_import', { token: '1' })
  })
  it('locks while the native picker is open and never double submits', async () => {
    let release!: (value: unknown) => void
    const call = vi.fn(() => new Promise(resolve => { release = resolve })), gateway = createDesktopGateway(async () => ready(), call)
    await gateway.read(); const first = gateway.chooseImport()
    await expect(gateway.confirmImport('1')).rejects.toThrow('重复提交')
    await expect(gateway.setPreference('DarkMode', false)).rejects.toThrow('重复提交')
    await expect(gateway.read()).rejects.toThrow('稍后刷新')
    release(previewFixture); await first; expect(call).toHaveBeenCalledTimes(1)
  })
  it('surfaces backend conflicts with actual state and blocks uncertain import until refresh', async () => {
    const reply = ready(); reply.OperationError = '预览后配置已变化'
    const call = vi.fn().mockResolvedValueOnce(reply).mockRejectedValueOnce({ message: '连接中断', requiresRefresh: true }).mockResolvedValue(null)
    const gateway = createDesktopGateway(async () => ready(), call); await gateway.read()
    await expect(gateway.confirmImport('1')).rejects.toMatchObject({ message: '预览后配置已变化', snapshot: { CanManageBackup: true }, requiresRefresh: false })
    await expect(gateway.confirmImport('2')).rejects.toMatchObject({ requiresRefresh: true })
    await expect(gateway.chooseImport()).rejects.toThrow('先刷新'); expect(call).toHaveBeenCalledTimes(2)
    await gateway.read(); expect(await gateway.chooseImport()).toBeNull()
  })
  it('never reports success without saved status and recoverable backup path', async () => {
    const reply = ready(); reply.BackupPath = ''
    const gateway = createDesktopGateway(async () => ready(), async () => reply); await gateway.read()
    await expect(gateway.confirmImport('1')).rejects.toMatchObject({ requiresRefresh: true })
  })
})
