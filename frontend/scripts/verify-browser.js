// Run with: playwright-cli -s=audio-ui run-code --filename scripts/verify-browser.js
// Uses an isolated browser session pointed at the local production preview.
async (page) => {
  const checks = []
  const errors = []
  const requests = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('request', request => requests.push(request.url()))
  const assert = (condition, label) => { if (!condition) throw new Error(label); checks.push(label) }
  const waitReady = () => page.getByRole('table', { name: '音频设备' }).waitFor()
  const capture = async (path) => {
    // Wait for the short focus/selection transitions before recording pixels.
    await page.waitForTimeout(180)
    await page.screenshot({ path })
  }
  const dismiss = async () => {
    const button = page.getByRole('button', { name: '关闭操作提示' })
    if (await button.count()) await button.click()
  }
  await page.setViewportSize({ width: 1100, height: 740 })
  await page.reload(); await waitReady()
  await capture('output/playwright/devices-dark.png')
  await page.getByRole('button', { name: '切换到浅色模式' }).click()
  await page.getByRole('button', { name: '切换到深色模式' }).waitFor()
  await dismiss()
  await capture('output/playwright/devices-light.png')
  await page.getByRole('button', { name: '切换到深色模式' }).click()
  await page.getByRole('button', { name: '切换到浅色模式' }).waitFor()
  await dismiss()
  await page.getByRole('button', { name: '麦克风输入', exact: true }).click()
  assert(await page.getByRole('table').getByRole('row').count() === 3, 'input filter')
  await page.getByRole('button', { name: '全部设备', exact: true }).click()
  await page.getByRole('button', { name: /^切换到 桌面扬声器/ }).click()
  await page.getByText(/已在预览中切换到/).waitFor()
  assert(await page.getByRole('region', { name: '当前声音输出' }).getByRole('heading').innerText() === '桌面扬声器', 'simulated switching')
  await dismiss()
  const trigger = page.getByRole('button', { name: /^设置 Studio 耳机/ })
  await trigger.click()
  await page.getByRole('dialog').waitFor()
  await capture('output/playwright/device-settings-dark.png')
  const focusStates = []
  for (let i = 0; i < 14; i++) {
    await page.keyboard.press('Tab')
    // Base UI redirects its focus guards on the next frame, like a real keypress.
    await page.waitForFunction(() => !!document.activeElement?.closest('[role="dialog"]'), null, { timeout: 1000 })
    focusStates.push(await page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]')))
  }
  assert(focusStates.every(Boolean), 'drawer keyboard focus remains inside')
  await page.keyboard.press('Escape')
  await page.getByRole('dialog').waitFor({ state: 'detached' })
  assert(await trigger.evaluate(el => el === document.activeElement), 'Escape restores focus to settings trigger')
  await trigger.click()
  await page.getByRole('switch', { name: '使用指定音量' }).click()
  await page.getByRole('button', { name: '取消', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'detached' })
  await trigger.click()
  assert(await page.getByRole('switch', { name: '使用指定音量' }).isChecked(), 'cancel discards draft')
  await page.getByRole('switch', { name: '使用指定音量' }).click()
  await page.getByLabel('空间音效预设').selectOption('off')
  await page.getByRole('button', { name: '仅保存（模拟）' }).click()
  await page.getByRole('dialog').waitFor({ state: 'detached' })
  assert(await page.getByRole('region', { name: '当前声音输出' }).getByRole('heading').innerText() === '桌面扬声器', 'save-only preserves routing')
  await dismiss()
  await page.getByRole('button', { name: '自动切换', exact: true }).click()
  await capture('output/playwright/automation-dark.png')
  await page.getByRole('button', { name: '上移 蓝牙耳机' }).click()
  await page.getByText(/已调整示例优先级/).waitFor(); await dismiss()
  await page.getByRole('button', { name: '应用设置', exact: true }).click()
  assert(await page.locator('main').evaluate(el => el.scrollTop) === 0, 'navigation resets content scroll')
  await capture('output/playwright/app-settings-dark.png')
  await page.getByRole('button', { name: /Dolby 编辑器/ }).click()
  assert(await page.getByText(/Dolby 高级编辑器暂未接入/).isVisible(), 'Dolby editor remains explicitly unavailable')
  await dismiss()
  await page.getByRole('button', { name: '声音设备', exact: true }).click()
  for (const scenario of ['empty', 'duplicate', 'long', 'loading', 'error']) {
    await page.getByLabel('预览场景').selectOption(scenario)
    if (scenario === 'loading') {
      await page.getByRole('heading', { name: '正在载入示例设备…' }).waitFor()
      await capture('output/playwright/loading-dark.png')
    }
    if (scenario === 'error') {
      await page.getByRole('heading', { name: '暂时无法显示设备' }).waitFor()
      await capture('output/playwright/error-dark.png')
      await page.getByRole('button', { name: '重试', exact: true }).click()
      await waitReady()
    } else {
      await waitReady()
      if (scenario !== 'loading') await capture(`output/playwright/${scenario}-dark.png`)
    }
    checks.push(`scenario: ${scenario}`)
  }
  const cdp = await page.context().newCDPSession(page)
  for (const base of [{ width: 1100, height: 740 }, { width: 980, height: 680 }]) {
    for (const scale of [1, 1.25, 1.5, 2]) {
      const width = Math.round(base.width / scale)
      const height = Math.round(base.height / scale)
      await page.setViewportSize({ width, height })
      await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: scale, mobile: false })
      for (const name of ['声音设备', '自动切换', '应用设置']) {
        await page.getByRole('button', { name, exact: true }).click()
        const geometry = await page.evaluate(() => {
          const main = document.querySelector('main')
          const footer = document.querySelector('.statusbar').getBoundingClientRect()
          return { noOverflow: document.documentElement.scrollWidth <= innerWidth && main.scrollWidth <= main.clientWidth, footerVisible: footer.bottom <= innerHeight + 1 }
        })
        assert(geometry.noOverflow && geometry.footerVisible, `${base.width}x${base.height} / ${scale * 100}% / ${name}: no horizontal clipping, footer visible`)
      }
      await page.getByRole('button', { name: '声音设备', exact: true }).click()
      await capture(`output/playwright/layout-${base.width}-${scale * 100}.png`)
      await page.getByRole('button', { name: /^设置 Studio 耳机/ }).click()
      await page.getByRole('dialog').waitFor()
      const box = await page.getByRole('button', { name: '仅保存（模拟）' }).boundingBox()
      assert(box && box.y >= 0 && box.y + box.height <= height, `drawer save visible at ${base.width} / ${scale * 100}%`)
      if (scale === 2) await capture(`output/playwright/drawer-${base.width}-200.png`)
      await page.keyboard.press('Escape')
      await page.getByRole('dialog').waitFor({ state: 'detached' })
    }
  }
  await cdp.send('Emulation.clearDeviceMetricsOverride')
  await cdp.detach()
  await page.setViewportSize({ width: 1100, height: 740 })
  await page.reload(); await waitReady()
  assert(errors.length === 0, 'no browser runtime errors')
  assert(requests.every(url => url.startsWith('http://127.0.0.1:4173/')), 'only local static asset requests')
  assert(await page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0), 'no browser settings persistence')
  return { checks, count: checks.length, errors, requestOrigins: [...new Set(requests.map(url => new URL(url).origin))] }
}
