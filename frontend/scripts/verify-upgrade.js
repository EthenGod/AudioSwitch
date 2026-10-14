// Playwright CLI against the browser's memory-only preview. No native IPC or real updates.
async (page) => {
  const checks = []
  const assert = (value, name) => { if (!value) throw new Error(name); checks.push(name) }
  const dialog = page.getByRole('dialog')
  await page.setViewportSize({ width:1100, height:740 })
  assert(await page.getByRole('heading', { name:'检查更新', exact:true }).evaluate(e => e === document.activeElement), 'initial focus is inside maintenance dialog')
  await page.getByRole('button', { name:'模拟检查', exact:true }).click()
  await page.getByRole('button', { name:'下载更新', exact:true }).waitFor()
  await page.getByRole('button', { name:'下载更新', exact:true }).click()
  await page.getByRole('progressbar', { name:'下载进度' }).waitFor()
  await page.getByRole('button', { name:'取消任务', exact:true }).click()
  await page.getByText('模拟任务已取消，没有修改文件或系统设置。', { exact:true }).waitFor()
  assert(await page.getByRole('button', { name:'准备安装', exact:true }).count() === 0, 'cancelled download offers no installation')
  await page.getByRole('button', { name:'重新检查', exact:true }).click()
  await page.getByRole('button', { name:'下载更新', exact:true }).click()
  await page.getByRole('button', { name:'准备安装', exact:true }).click()
  await page.getByRole('region', { name:'安装确认' }).waitFor()
  assert(await page.getByRole('button', { name:'模拟确认安装', exact:true }).isEnabled(), 'separate final confirmation required')
  for (const theme of ['dark', 'light']) {
    await page.evaluate(theme => document.documentElement.classList.toggle('dark', theme === 'dark'), theme)
    for (const [width, height] of [[1100,740], [980,680]]) {
      await page.setViewportSize({ width, height })
      for (const zoom of [1, 1.25, 1.5, 2]) {
        await page.evaluate(zoom => { document.documentElement.style.zoom = String(zoom) }, zoom)
        const layout = await dialog.evaluate(e => {
          const r = e.getBoundingClientRect()
          return { overflow:e.scrollWidth > e.clientWidth + 1,
            visible:[...e.querySelectorAll('.sheet-footer button')].every(button => { const b = button.getBoundingClientRect(); return b.x >= r.x - 1 && b.right <= innerWidth + 1 && b.y >= 0 && b.bottom <= innerHeight + 1 }) }
        })
        assert(!layout.overflow && layout.visible, `${theme} ${width}x${height} CSS zoom ${zoom}: confirmation buttons fit`)
        if ((width === 1100 && zoom === 1) || (width === 980 && zoom === 2)) await page.screenshot({ path:`output/playwright/upgrade-${theme}-${width}-${zoom}.png` })
      }
    }
  }
  await page.evaluate(() => { document.documentElement.style.zoom = '1' })
  await page.setViewportSize({ width:1100, height:740 })
  await page.getByRole('button', { name:'暂不安装', exact:true }).click()
  assert(await page.getByRole('button', { name:'准备安装', exact:true }).isVisible(), 'declining confirmation keeps prepared payload')
  await page.getByRole('button', { name:'准备安装', exact:true }).click()
  await page.getByRole('button', { name:'模拟确认安装', exact:true }).focus()
  await page.keyboard.press('Enter')
  await page.getByText('模拟交接结束。没有安装、退出或重启任何程序。', { exact:true }).waitFor()
  assert(await page.getByRole('button', { name:'模拟确认安装', exact:true }).count() === 0, 'keyboard confirmation cannot be resubmitted')
  await page.getByRole('button', { name:'关闭', exact:true }).click()
  await dialog.waitFor({ state:'detached' })
  console.log(JSON.stringify({ checks, total:checks.length, boundary:'CSS zoom preview only; no real network, files, audio or installation' }))
}
