import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Script } from 'node:vm'
import path from 'node:path'
import { JSDOM } from 'jsdom'
import { waitFor, within } from '@testing-library/dom'
import { inlinePreviewScript } from './preview-document.mjs'

test('HTML-like dependency strings cannot swallow the license or escape the module', () => {
  const code = 'globalThis.fixture = `</body><link rel="stylesheet" href="x"><script src="x"></script>`;'
  const template = '<!doctype html><html><head></head><body><div id="root"></div><script type="module" src="/entry.js"></script><script type="application/json" id="preview-licenses">"license"</script></body></html>'
  const document = new JSDOM(inlinePreviewScript(template, code)).window.document
  assert.equal(document.scripts.length, 2)
  assert.equal(document.querySelector('#preview-licenses').parentElement, document.body)
  assert.equal(JSON.parse(document.querySelector('#preview-licenses').textContent), 'license')
  const context = {}
  new Script(document.querySelector('script[type=module]').textContent).runInNewContext(context)
  assert.equal(context.fixture, '</body><link rel="stylesheet" href="x"><script src="x"></script>')
  assert.equal(document.querySelectorAll('link').length, 0)
})

test('generated preview contains a complete executable bundle, root and separate license', async () => {
  const file = process.argv[2] ? path.resolve(process.argv[2]) : new URL('../../staging/ui-preview/声间-UI预览.html', import.meta.url)
  const html = await readFile(file, 'utf8')
  const dom = new JSDOM(html, {
    url: 'https://preview.invalid/', pretendToBeVisual: true, runScripts: 'outside-only',
    beforeParse(window) {
      window.matchMedia = media => ({ matches: false, media, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} })
      window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} }
      window.structuredClone = structuredClone
    },
  })
  const document = dom.window.document
  try {
  assert.equal(document.scripts.length, 2)
  assert.equal(document.querySelectorAll('#root').length, 1)
  const module = document.querySelector('script[type=module]')
  assert.ok(module.textContent.length > 1000)
  // This preview bundle has no imports or exports. Parsing it catches premature HTML termination.
  new Script(module.textContent)
  const licenses = document.querySelector('#preview-licenses')
  assert.equal(licenses.parentElement, document.body)
  assert.match(JSON.parse(licenses.textContent), /DOMPurify/i)
  assert.equal([...document.body.childNodes].filter(node => node.nodeType === 3).map(node => node.textContent).join('').trim(), '')
  assert.equal(document.querySelectorAll('script[src],link[rel=stylesheet],link[rel=modulepreload]').length, 0)
  assert.match(document.querySelector('meta[http-equiv="Content-Security-Policy"]').content, /connect-src 'none'/)
  // Execute only this trusted local bundle in a DOM test environment; external resources are disabled.
  dom.window.eval(module.textContent)
  await waitFor(() => assert.ok(within(document.body).getByRole('table')), { container: document.body, timeout: 3000 })
  within(document.body).getByRole('button', { name: '应用设置', exact: true }).click()
  await waitFor(() => assert.ok(within(document.body).getByRole('button', { name: /检查更新/ })), { container: document.body })
  assert.match(document.body.textContent, /界面预览/)
  } finally { dom.window.close() }
})
