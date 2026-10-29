// Run via Playwright CLI attached to the extracted candidate, without a backend.
async (page) => {
  const errors = [], requests = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('request', request => requests.push(request.url()))
  await page.reload()
  await page.getByText('未连接到声间后台。请先打开原版声间，再点击重试。', { exact: true }).waitFor()
  if (!(await page.getByRole('button', { name: '重试', exact: true }).isEnabled())) throw new Error('Retry is not available')
  await page.getByRole('button', { name: '重试', exact: true }).click()
  await page.getByText('未连接到声间后台。请先打开原版声间，再点击重试。', { exact: true }).waitFor()
  if (errors.length) throw new Error(errors.join('\n'))
  const remote = requests.filter(url => !url.startsWith('http://tauri.localhost/') && !url.startsWith('http://ipc.localhost/') && !url.startsWith('ipc://'))
  if (remote.length) throw new Error(`Unexpected page requests: ${remote.join(', ')}`)
  await page.screenshot({ path: 'output/desktop/stage51-candidate.png' })
  return { checks: 4, noBackendMessage: true, retryAvailable: true, runtimeErrors: errors, remotePageRequests: remote }
}
