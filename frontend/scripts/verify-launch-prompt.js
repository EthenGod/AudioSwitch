// Attach to the prompt tab of --frontend-launch-host, never a real audio session.
async (page) => {
  await page.getByRole('paragraph').filter({ hasText: /^隔离验证设备（不会切换音频）$/ }).waitFor()
  const context = page.context()
  const main = context.pages().find(other => other !== page)
  if (!main) throw new Error('Expected the existing main panel')
  await main.getByText('后台提示：隔离启动验证：模拟设备，禁止音频及配置写入。', { exact: true }).waitFor()
  await page.screenshot({ path: 'output/desktop/stage52-prompt.png' })
  const closed = page.waitForEvent('close', { timeout: 10000 })
  await page.getByRole('button', { name: '打开面板', exact: true }).click().catch(error => { if (!page.isClosed()) throw error })
  await closed
  if (main.isClosed() || context.pages().length !== 1 || context.pages()[0] !== main) throw new Error('Existing panel was not reused')
  await main.screenshot({ path: 'output/desktop/stage52-panel.png' })
  return { promptClosed: true, existingPanelReused: true, openPages: context.pages().length }
}
