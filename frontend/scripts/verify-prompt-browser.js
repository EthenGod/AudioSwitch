// Run with playwright-cli run-code --filename scripts/verify-prompt-browser.js.
async (page) => {
  const checks = [], errors = [], requests = []
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label) }
  page.on('pageerror', e => errors.push(e.message)); page.on('request', r => requests.push(r.url()))
  for (const light of [false, true]) {
    for (const disconnected of [false, true]) {
      await page.goto(`http://127.0.0.1:4173/?view=prompt${light ? '&light' : ''}${disconnected ? '&disconnected' : ''}`)
      await page.getByRole('combobox').waitFor()
      check(await page.locator('html').evaluate(el => el.classList.contains('dark')) === !light, 'theme follows preview setting')
      check(await page.getByText('界面预览，操作不会改变系统设置').isVisible(), 'explicit preview banner')
      for (const scale of [1, 1.25, 1.5, 2]) {
        await page.setViewportSize({ width: Math.round(400 / scale), height: Math.round(380 / scale) })
        check(await page.locator('.prompt-shell').evaluate(el => {
          const footer = el.querySelector('.prompt-actions').getBoundingClientRect()
          return el.scrollWidth <= el.clientWidth + 1 && footer.top >= 0 && footer.bottom <= innerHeight + 1
        }), `${light ? 'light' : 'dark'}/${disconnected ? 'lost' : 'new'}/${scale}: footer visible, no horizontal overflow`)
      }
      await page.setViewportSize({ width: 400, height: 380 })
      await page.screenshot({ path: `output/playwright/prompt-${light ? 'light' : 'dark'}-${disconnected ? 'lost' : 'new'}.png` })
      const keep = page.getByRole('button', { name: '保持当前选择', exact: true })
      await keep.focus()
      check(await keep.evaluate(el => el.matches(':focus-visible') && getComputedStyle(el).outlineStyle !== 'none'), 'keyboard focus visible')
      await page.keyboard.press('Enter'); await page.getByText('提示已关闭', { exact: true }).waitFor()
      check(await page.getByRole('button', { name: '保持当前选择', exact: true }).count() === 0, 'acknowledgment removes actions')
    }
  }
  await page.goto('http://127.0.0.1:4173/?view=prompt'); await page.getByRole('combobox').waitFor()
  await page.getByRole('button', { name: '用新设备', exact: true }).click(); await page.getByText('提示已关闭', { exact: true }).waitFor()
  check(errors.length === 0, 'no runtime errors')
  check(requests.every(url => url.startsWith('http://127.0.0.1:4173/')), 'mock only requested local assets')
  return { count: checks.length, checks, errors }
}
