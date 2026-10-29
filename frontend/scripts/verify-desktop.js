// Playwright CLI run-code, attached to this panel's temporary local WebView2 CDP endpoint.
// Only read_snapshot reaches the native bridge. Disconnect is injected in this test page.
async (page) => {
  const checks = [], errors = []
  const assert = (value, label) => { if (!value) throw new Error(label); checks.push(label) }
  page.on('pageerror', error => errors.push(error.message))
  await page.getByRole('table', { name: '音频设备' }).waitFor()
  const before = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('read_snapshot'))
  const deviceCount = await page.locator('[data-device-id]').count()
  const expectedIds = new Set([...before.Preferences.DeviceOrder, ...before.State.Devices].map(device => device.Id))
  assert(deviceCount === expectedIds.size, 'real backend online and remembered endpoints match rendered rows')
  const ids = await page.locator('[data-device-id]').evaluateAll(rows => rows.map(row => row.dataset.deviceId))
  assert(ids.every(id => expectedIds.has(id)), 'real endpoint IDs retained')
  for (const button of await page.getByRole('button', { name: /^(切换到|正在使用) / }).all()) {
    assert(await button.isDisabled(), 'real device mutation disabled')
  }
  assert(await page.getByText('只读连接，仅显示真实状态，不会改变系统设置').isVisible(), 'read-only status visible')
  await page.screenshot({ path: 'output/desktop/devices-dark.png' })
  await page.getByRole('button', { name: '切换到浅色模式' }).click()
  await page.screenshot({ path: 'output/desktop/devices-light.png' })
  await page.getByRole('button', { name: '切换到深色模式' }).click()
  await page.getByRole('button', { name: '设置当前输出' }).click()
  const sheet = page.getByRole('dialog')
  await sheet.waitFor()
  assert(await sheet.getByRole('switch').getAttribute('aria-disabled') === 'true', 'saved-volume control disabled')
  assert(await sheet.getByRole('button', { name: /保存/ }).count() === 0, 'no save action in read-only drawer')
  for (let i = 0; i < 5; i++) {
    await page.keyboard.press('Tab')
    await page.waitForFunction(() => !!document.activeElement.closest('[role="dialog"]'))
  }
  checks.push('drawer keyboard focus stays inside')
  await page.screenshot({ path: 'output/desktop/device-settings.png' })
  await page.keyboard.press('Escape')
  await sheet.waitFor({ state: 'hidden' })
  await page.getByRole('button', { name: '自动切换', exact: true }).click()
  for (const control of await page.getByRole('switch').all()) assert(await control.getAttribute('aria-disabled') === 'true', 'automation toggle disabled')
  for (const button of await page.getByRole('button', { name: /^(上移|下移) / }).all()) assert(await button.isDisabled(), 'real priority editing disabled')
  await page.screenshot({ path: 'output/desktop/automation.png' })
  await page.getByRole('button', { name: '应用设置', exact: true }).click()
  for (const control of await page.getByRole('switch').all()) assert(await control.getAttribute('aria-disabled') === 'true', 'real application setting disabled')
  await page.screenshot({ path: 'output/desktop/settings.png' })
  await page.getByRole('button', { name: '声音设备', exact: true }).click()
  await page.getByRole('button', { name: '麦克风输入', exact: true }).click()
  const inputIds = new Set([...before.Preferences.DeviceOrder, ...before.State.Devices].filter(d => d.Flow === 1).map(d => d.Id))
  assert(await page.locator('[data-device-id]').count() === inputIds.size, 'real input filter')
  await page.getByRole('button', { name: '全部设备', exact: true }).click()
  const originalInvoke = await page.evaluate(() => {
    window.__originalFetchForTest = window.fetch
    window.fetch = (url, options) => String(url).includes('ipc.localhost/read_snapshot')
      ? Promise.resolve(new Response(JSON.stringify('测试注入：后台连接已中断'), { headers: { 'Content-Type': 'application/json', 'Tauri-Response': 'error' } }))
      : window.__originalFetchForTest(url, options)
    return true
  })
  try {
    assert(originalInvoke, 'test transport installed without stopping backend')
    await page.getByRole('button', { name: '刷新状态' }).click()
    await page.getByText('测试注入：后台连接已中断').waitFor()
    assert(await page.getByRole('table').count() === 0, 'disconnection clears stale rows')
    await page.screenshot({ path: 'output/desktop/disconnected-injected.png' })
  } finally {
    await page.evaluate(() => { window.fetch = window.__originalFetchForTest; delete window.__originalFetchForTest })
  }
  await page.getByRole('button', { name: '重试', exact: true }).click()
  await page.getByRole('table').waitFor()
  checks.push('retry reconnects to real backend')
  const after = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('read_snapshot'))
  assert(JSON.stringify(before.State.Defaults) === JSON.stringify(after.State.Defaults), 'system defaults unchanged throughout UI checks')
  assert(JSON.stringify(before.Preferences) === JSON.stringify(after.Preferences), 'backend preferences unchanged including theme')
  assert(before.Startup.Enabled === after.Startup.Enabled, 'startup registration unchanged')
  assert(await page.evaluate(() => !localStorage.length && !sessionStorage.length), 'no application settings in browser storage')
  assert(errors.length === 0, 'no WebView runtime errors')
  return { count: checks.length, checks, errors, onlineDevices: before.State.Devices.length, totalDevices: expectedIds.size }
}
