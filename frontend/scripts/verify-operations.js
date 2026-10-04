// Playwright CLI, attached to the dedicated test panel. All IPC is intercepted.
// This verifies production UI behavior with isolated fixtures, NOT real audio writes.
async (page) => {
  const checks = []
  const assert = (value, label) => { if (!value) throw new Error(label); checks.push(label) }
  await page.evaluate(() => {
    if (window.__stage3Fixture) throw new Error('Fixture already installed')
    const data = {
      PanelApiVersion: 1, OperationError: null, Error: null, Warning: null,
      State: { Devices: [{ Id: 'fixture-a', Name: '隔离验收扬声器', Flow: 0 }, { Id: 'fixture-b', Name: '隔离验收耳机', Flow: 0 }], Defaults: { '0:0': 'fixture-a', '0:1': 'fixture-a', '0:2': 'fixture-a' } },
      Preferences: { DarkMode: true, GameMode: false, AskOnConnect: true, IncludeCommunications: false, UseDevicePriority: true, AutoUpdateEnabled: true,
        DeviceOrder: [{ Id: 'fixture-a', Name: '隔离验收扬声器', Flow: 0 }, { Id: 'fixture-b', Name: '隔离验收耳机', Flow: 0 }, { Id: 'fixture-offline', Name: '隔离验收离线设备', Flow: 0 }],
        DeviceProfiles: {}, DeviceRules: {} },
      Startup: { Enabled: false, Available: true, RegisteredCommand: null, Message: '隔离验收数据，不修改启动项' }, DolbyApplying: false,
    }
    const test = window.__stage3Fixture = { data, actions: [], failNext: false, originalFetch: window.fetch }
    window.fetch = async (url, options) => {
      if (!String(url).includes('ipc.localhost')) return test.originalFetch(url, options)
      const command = new URL(String(url)).pathname.slice(1)
      const payload = options.body ? JSON.parse(options.body) : {}
      let result = data, okay = true
      if (command === 'read_device_settings') result = { DeviceSettings: { CurrentVolume: 42, VolumeError: null, SpatialError: null,
        Spatial: { Supported: true, CurrentFormat: '', Options: [{ Id: '', Name: '关闭空间音效' }, { Id: '{fixture-format}', Name: '隔离驱动选项' }] } } }
      else if (command === 'panel_action') {
        const action = payload.action; test.actions.push(action)
        if (test.failNext) { test.failNext = false; okay = false; result = { message: '隔离验收：结果未确认，请先刷新。', requiresRefresh: true } }
        else if (action.kind === 'switch') { data.State.Defaults['0:0'] = data.State.Defaults['0:1'] = action.id; if (data.Preferences.IncludeCommunications) data.State.Defaults['0:2'] = action.id }
        else if (action.kind === 'preference') data.Preferences[action.key] = action.value
        else if (action.kind === 'saveBasic') { data.Preferences.DeviceProfiles[action.id] = action.profile; data.Preferences.DeviceRules[action.id] = action.rule }
        else if (action.kind === 'reorder') data.Preferences.DeviceOrder.sort((a, b) => action.ids.indexOf(a.Id) - action.ids.indexOf(b.Id))
        else if (action.kind === 'startup') data.Startup.Enabled = action.value
        else throw new Error('Unexpected fixture action')
      } else if (command !== 'read_snapshot') { okay = false; result = { message: 'Fixture rejects unknown IPC', requiresRefresh: false } }
      return new Response(JSON.stringify(result), { headers: { 'Content-Type': 'application/json', 'Tauri-Response': okay ? 'ok' : 'error' } })
    }
  })
  try {
    await page.getByRole('button', { name: '刷新状态' }).click()
    await page.getByRole('table').waitFor()
    assert(await page.getByText('已连接真实后台，操作会改变系统设置').isVisible(), 'write-capable mode clearly differs from preview')
    await page.getByRole('button', { name: /切换到 隔离验收耳机/ }).click()
    await page.getByText(/已切换到「隔离验收耳机」/).waitFor()
    assert(await page.evaluate(() => window.__stage3Fixture.data.State.Defaults['0:2'] === 'fixture-a'), 'switch excludes communications when disabled')
    await page.getByRole('button', { name: /设置 隔离验收耳机/ }).click()
    await page.getByRole('option', { name: '隔离驱动选项' }).waitFor({ state: 'attached' })
    await page.getByRole('switch', { name: '使用指定音量' }).click()
    await page.getByLabel('空间音效预设').selectOption('off')
    await page.getByLabel('白名单规则').selectOption('1')
    await page.screenshot({ path: 'output/desktop/stage3-drawer-fixture.png' })
    await page.getByRole('button', { name: '仅保存', exact: true }).click()
    await page.getByRole('dialog').waitFor({ state: 'detached' })
    const save = await page.evaluate(() => window.__stage3Fixture.actions.at(-1))
    assert(save.kind === 'saveBasic' && save.expectedProfile === null && save.profile.Volume === 50 && save.profile.SpatialFormat === '' && save.rule === 1, 'save retains baseline, zero/off semantics and separate rule')
    assert(await page.evaluate(() => window.__stage3Fixture.data.State.Defaults['0:1'] === 'fixture-b'), 'save does not send route operation')
    await page.getByRole('button', { name: '自动切换', exact: true }).click()
    await page.getByRole('button', { name: '上移 隔离验收离线设备' }).click()
    await page.getByText(/排序已保存/).waitFor()
    assert(await page.evaluate(() => window.__stage3Fixture.data.Preferences.DeviceOrder[1].Id === 'fixture-offline'), 'offline priority can move')
    await page.getByRole('button', { name: '应用设置', exact: true }).click()
    await page.getByRole('switch', { name: '开机自启' }).click()
    await page.getByText('Windows 自启设置已更新。').waitFor()
    await page.getByRole('button', { name: '浅色模式', exact: true }).click()
    await page.getByText('设置已保存。', { exact: true }).waitFor()
    assert(await page.evaluate(() => !document.documentElement.classList.contains('dark')), 'confirmed theme applied')
    await page.screenshot({ path: 'output/desktop/stage3-settings-light-fixture.png' })
    await page.evaluate(() => { window.__stage3Fixture.failNext = true })
    await page.getByRole('switch', { name: '游戏模式' }).click()
    await page.getByText('隔离验收：结果未确认，请先刷新。').waitFor()
    assert(!await page.getByRole('switch', { name: '游戏模式' }).isChecked(), 'failed preference is not optimistic')
    assert(await page.getByRole('switch', { name: '游戏模式' }).getAttribute('aria-disabled') === 'true', 'uncertain result locks writes')
    await page.getByRole('button', { name: '声音设备', exact: true }).click()
    await page.getByRole('button', { name: '刷新状态' }).click()
    await page.getByRole('table').waitFor()
    assert(await page.getByText('已连接真实后台，操作会改变系统设置').isVisible(), 'explicit refresh clears uncertain state')
    return { count: checks.length, checks, source: 'isolated fixtures; all IPC intercepted; no real audio writes' }
  } finally {
    await page.evaluate(() => { window.fetch = window.__stage3Fixture.originalFetch; delete window.__stage3Fixture })
    await page.reload()
  }
}
