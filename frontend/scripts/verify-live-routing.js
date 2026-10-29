// OPT-IN staging-only hardware check. No changed preset, stale-save probe,
// priority reorder, registry write, or Dolby editor write. Finally restores routes/preferences.
async (page) => {
  await page.getByRole('table').waitFor()
  const checks = [], restoration = []
  const invoke = async (command, args = {}) => {
    const r = await page.evaluate(async ({ command, args }) => {
      try { return { value: await window.__TAURI_INTERNALS__.invoke(command, args) } }
      catch (error) { return { error } }
    }, { command, args })
    if (r.error) throw new Error(JSON.stringify(r.error))
    return r.value
  }
  const read = () => invoke('read_snapshot')
  const action = async a => { const r = await invoke('panel_action', { action: a }); if (r.OperationError) throw new Error(r.OperationError); return r }
  const assert = (value, label) => { if (!value) throw new Error(label); checks.push(label) }
  const settled = async () => { for (let i = 0; i < 40; i++) { const s = await read(); if (!s.DolbyApplying) return s; await page.waitForTimeout(250) } throw new Error('Dolby still running') }
  const before = await settled(), ids = [before.State.Defaults['0:1'], before.State.Defaults['1:1']]
  assert(before.PanelApiVersion >= 1 && /[\\/]staging[\\/]AudioSwitch\.exe$/i.test(before.BackendExecutablePath), 'correct staging backend')
  for (const flow of [0, 1]) assert([0, 1, 2].every(role => before.State.Defaults[`${flow}:${role}`] === ids[flow]), `flow ${flow} original roles unified`)
  const targets = [0, 1].map(flow => before.State.Devices.find(d => d.Flow === flow && d.Id !== ids[flow] && !before.Preferences.DeviceProfiles[d.Id]))
  assert(targets.every(Boolean), 'both temporary targets have no preset to apply')
  const profile = before.Preferences.DeviceProfiles[ids[0]]
  assert(!!profile, 'existing profile for unchanged save check')
  await page.evaluate(() => { window.__liveFocusGuard = e => e.stopImmediatePropagation(); window.addEventListener('focus', window.__liveFocusGuard, true) })
  let failure
  try {
    const basic = { Volume: profile.Volume, SpatialFormat: profile.SpatialFormat }, rule = before.Preferences.DeviceRules[ids[0]] ?? 0
    const live = await invoke('read_device_settings', { id: ids[0] })
    const same = await action({ kind: 'saveBasic', id: ids[0], profile: basic, expectedProfile: basic, rule, expectedRule: rule })
    const reread = await invoke('read_device_settings', { id: ids[0] })
    assert(JSON.stringify(same.Preferences) === JSON.stringify(before.Preferences) && live.DeviceSettings.CurrentVolume === reread.DeviceSettings.CurrentVolume && live.DeviceSettings.Spatial.CurrentFormat === reread.DeviceSettings.Spatial.CurrentFormat, 'unchanged save traverses native bridge and preserves preset/live values')
    await action({ kind: 'preference', key: 'IncludeCommunications', value: false })
    await page.getByRole('button', { name: '刷新状态' }).click(); await page.getByRole('table').waitFor()
    for (const flow of [0, 1]) {
      const target = targets[flow]
      await page.getByRole('button', { name: `切换到 ${target.Name} · ${flow === 0 ? '声音输出' : '麦克风输入'}`, exact: true }).click()
      await page.getByText(`已切换到「${target.Name}」。音效结果请查看后台提示。`, { exact: true }).waitFor()
      const actual = await settled()
      assert(actual.State.Defaults[`${flow}:0`] === target.Id && actual.State.Defaults[`${flow}:1`] === target.Id && actual.State.Defaults[`${flow}:2`] === ids[flow], `real UI flow ${flow} switch preserves communications`)
    }
    await page.getByRole('button', { name: before.Preferences.DarkMode ? '切换到浅色模式' : '切换到深色模式', exact: true }).click()
    await page.getByText('设置已保存。', { exact: true }).waitFor()
    assert((await read()).Preferences.DarkMode !== before.Preferences.DarkMode, 'real theme toggle persists')
    await page.screenshot({ path: 'output/desktop/stage3-live-routing.png' })
  } catch (error) { failure = String(error) }
  finally {
    const restore = async (label, job) => { try { await job() } catch (error) { restoration.push(`${label}: ${error}`) } }
    await restore('refresh', read)
    for (const id of ids) await restore('route', () => action({ kind: 'switch', id }))
    await restore('communications', () => action({ kind: 'preference', key: 'IncludeCommunications', value: before.Preferences.IncludeCommunications }))
    await restore('theme', () => action({ kind: 'preference', key: 'DarkMode', value: before.Preferences.DarkMode }))
    await restore('Dolby settle', settled)
    await page.evaluate(() => { window.removeEventListener('focus', window.__liveFocusGuard, true); delete window.__liveFocusGuard })
  }
  const after = await read()
  const restored = JSON.stringify(after.Preferences) === JSON.stringify(before.Preferences) && JSON.stringify(after.State.Defaults) === JSON.stringify(before.State.Defaults) && after.Startup.RegisteredCommand === before.Startup.RegisteredCommand
  if (failure || restoration.length || !restored) throw new Error(JSON.stringify({ failure, restoration, restored, checks }))
  checks.push('complete preferences, six roles and startup command restored/unchanged')
  return { count: checks.length, checks, backendPid: after.BackendPid, source: 'real native bridge and hardware routing' }
}
