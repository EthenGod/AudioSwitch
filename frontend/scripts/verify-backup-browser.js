// playwright-cli -s=audio-ui run-code --filename scripts/verify-backup-browser.js
// Browser simulation only; no native transport or real files.
async (page) => {
  const checks = [], errors = []
  page.on('pageerror', error => errors.push(error.message))
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label) }
  await page.setViewportSize({ width: 1100, height: 740 })
  await page.reload(); await page.getByRole('table').waitFor()
  await page.getByRole('button', { name: '应用设置', exact: true }).click()
  for (const theme of ['深色模式', '浅色模式']) {
    await page.getByRole('button', { name: theme, exact: true }).click()
    await page.getByRole('button', { name: theme, exact: true }).and(page.locator('[aria-pressed="true"]')).waitFor()
    await page.getByRole('button', { name: /导入设置/ }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: '选择示例备份' }).click()
    await dialog.getByText('示例备份.json').waitFor()
    check(await dialog.getByText('6 台（当前离线 1 台）').isVisible(), `${theme}: summary includes offline devices`)
    await page.waitForTimeout(180)
    await page.screenshot({ path: `output/playwright/backup-${theme === '深色模式' ? 'dark' : 'light'}.png` })
    for (const base of [{ width: 1100, height: 740 }, { width: 980, height: 680 }]) {
      for (const scale of [1, 1.25, 1.5, 2]) {
        const width = Math.round(base.width / scale), height = Math.round(base.height / scale)
        await page.setViewportSize({ width, height })
        const geometry = await dialog.evaluate(element => {
          const rect = element.getBoundingClientRect(), footer = element.querySelector('.sheet-footer').getBoundingClientRect()
          return { noClip: element.scrollWidth <= element.clientWidth + 1 && rect.right <= innerWidth + 1 && rect.left >= -1,
            buttonsVisible: footer.bottom <= innerHeight + 1 && footer.top >= 0 }
        })
        check(geometry.noClip && geometry.buttonsVisible, `${theme}: ${base.width}/${scale * 100}% backup buttons visible`)
      }
    }
    await page.setViewportSize({ width: 1100, height: 740 })
    await page.keyboard.press('Escape'); await dialog.waitFor({ state: 'detached' })
    check(await page.getByRole('button', { name: /导入设置/ }).evaluate(el => el === document.activeElement), `${theme}: Escape returns keyboard focus`)
  }
  await page.getByRole('button', { name: /导入设置/ }).click()
  await page.getByRole('button', { name: '选择示例备份' }).click()
  await page.getByRole('button', { name: '确认导入（模拟）' }).click()
  await page.getByText('模拟流程已完成').waitFor()
  check(await page.getByText('模拟预览：没有读写文件或系统配置').isVisible(), 'import explicitly remains simulation')
  await page.getByRole('button', { name: '完成', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'detached' })
  await page.getByRole('button', { name: /导出备份/ }).click()
  await page.getByRole('button', { name: '模拟导出' }).click()
  await page.getByText('模拟预览：没有生成文件').waitFor()
  check(await page.getByText('模拟流程已完成').isVisible(), 'export never claims a real file exists')
  await page.keyboard.press('Escape')
  check(errors.length === 0, 'no browser runtime errors')
  return { count: checks.length, checks, errors }
}
