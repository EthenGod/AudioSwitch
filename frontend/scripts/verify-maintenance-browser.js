// Run via playwright-cli in the isolated local browser preview.
async (page) => {
  const checks = [], errors = [], requests = []
  page.on('pageerror', e => errors.push(e.message)); page.on('request', r => requests.push(r.url()))
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label) }
  await page.setViewportSize({ width: 1100, height: 740 }); await page.reload(); await page.getByRole('table').waitFor()
  await page.getByRole('button', { name: '应用设置', exact: true }).click()
  for (const theme of ['深色模式', '浅色模式']) {
    await page.getByRole('button', { name: theme, exact: true }).click()
    await page.getByRole('button', { name: theme, exact: true }).and(page.locator('[aria-pressed="true"]')).waitFor()
    for (const title of ['检查更新', '文件检查']) {
      const trigger = page.getByRole('button', { name: new RegExp(title) })
      await trigger.click(); const dialog = page.getByRole('dialog')
      check(await dialog.getByRole('button', { name: '模拟检查' }).isEnabled(), `${theme}/${title}: explicit start`)
      await dialog.getByRole('button', { name: '模拟检查' }).click()
      await dialog.getByRole('button', { name: '取消检查' }).click()
      await dialog.getByText('模拟检查已取消。').waitFor()
      check(await dialog.getByRole('button', { name: '重新检查' }).isEnabled(), `${theme}/${title}: cancellation and retry`)
      await dialog.getByRole('button', { name: '重新检查' }).click()
      await dialog.getByRole('heading', { name: title === '检查更新' ? '模拟发现新版本，未连接 GitHub。' : '模拟检查通过，未读取本机文件。' }).waitFor()
      await page.screenshot({ path: `output/playwright/maintenance-${title === '检查更新' ? 'update' : 'files'}-${theme === '深色模式' ? 'dark' : 'light'}.png` })
      for (const base of [{ width: 1100, height: 740 }, { width: 980, height: 680 }]) {
        for (const scale of [1, 1.25, 1.5, 2]) {
          await page.setViewportSize({ width: Math.round(base.width / scale), height: Math.round(base.height / scale) })
          check(await dialog.evaluate(el => {
            const footer = el.querySelector('.sheet-footer').getBoundingClientRect()
            return el.scrollWidth <= el.clientWidth + 1 && footer.top >= 0 && footer.bottom <= innerHeight + 1
          }), `${theme}/${title}/${base.width}/${scale * 100}%: no clipping, footer visible`)
        }
      }
      await page.setViewportSize({ width: 1100, height: 740 }); await page.keyboard.press('Escape'); await dialog.waitFor({ state: 'detached' })
      check(await trigger.evaluate(el => el === document.activeElement), `${theme}/${title}: focus restored`)
    }
  }
  check(errors.length === 0, 'no runtime errors')
  check(requests.every(url => url.startsWith('http://127.0.0.1:4173/')), 'simulation requests only local assets')
  return { count: checks.length, checks, errors }
}
