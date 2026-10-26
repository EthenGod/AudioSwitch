import { readFile, readdir, mkdir, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { build } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const staging = path.resolve(root, '../staging')
const destination = path.resolve(root, process.argv[2] || '../staging/ui-preview/声间-UI预览.html')
const relative = path.relative(staging, destination)
if (relative.startsWith('..') || path.isAbsolute(relative) || !relative.endsWith('.html')) {
  throw new Error('预览 HTML 必须放在项目 staging 目录内。')
}

const result = await build({
  root,
  configFile: path.join(root, 'vite.config.ts'),
  build: {
    write: false,
    emptyOutDir: false,
    modulePreload: false,
    rollupOptions: {
      input: path.join(root, 'preview.html'),
      output: { codeSplitting: false },
    },
  },
})
if (Array.isArray(result) || !('output' in result)) throw new Error('预览构建输出不符合预期。')
const output = result.output
const chunks = output.filter(item => item.type === 'chunk')
if (chunks.length !== 1 || chunks[0].imports.length || chunks[0].dynamicImports.length) {
  throw new Error('预览必须只有一个不依赖外部脚本的入口。')
}
const htmlAsset = output.find(item => item.type === 'asset' && item.fileName === 'preview.html')
if (!htmlAsset) throw new Error('缺少预览页面。')
const logo = `data:image/svg+xml;base64,${(await readFile(path.join(root, 'public/mark.svg'))).toString('base64')}`
const inlineLogo = text => text.replaceAll('/mark.svg', logo)
let html = String(htmlAsset.source)
let scriptCount = 0
html = html.replace(/<script\b[^>]*\bsrc="[^"]+"[^>]*><\/script>/g, () => {
  scriptCount++
  return `<script type="module">${inlineLogo(chunks[0].code).replace(/<\/script/gi, '<\\/script')}</script>`
})
if (scriptCount !== 1) throw new Error('预览脚本数量不符合预期。')
html = html.replace(/<link\b[^>]*rel="stylesheet"[^>]*>/g, tag => {
  const href = tag.match(/href="([^"]+)"/)?.[1]?.replace(/^\//, '')
  const css = output.find(item => item.type === 'asset' && item.fileName === href)
  if (!css) throw new Error(`缺少样式：${href}`)
  return `<style>${String(css.source).replace(/<\/style/gi, '<\\/style')}</style>`
})
html = inlineLogo(html)
// Network access is unnecessary. Blocking it also protects the mock-only boundary.
const policy = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'"
html = html.replace('<meta charset="UTF-8" />', `<meta charset="UTF-8" /><meta http-equiv="Content-Security-Policy" content="${policy}" />`)

const lock = JSON.parse(await readFile(path.join(root, 'package-lock.json'), 'utf8'))
const licenses = [await readFile(path.join(root, '../LICENSE'), 'utf8'), await readFile(path.join(root, 'THIRD-PARTY.md'), 'utf8')]
for (const [directory, entry] of Object.entries(lock.packages)) {
  if (!directory || entry.dev || entry.devOptional) continue
  const packageRoot = path.join(root, directory)
  const manifest = JSON.parse(await readFile(path.join(packageRoot, 'package.json'), 'utf8'))
  const files = (await readdir(packageRoot)).filter(name => /^(licen[sc]e|copying)(\..*)?$/i.test(name))
  if (!files.length) throw new Error(`缺少许可：${manifest.name}`)
  licenses.push(`\n${manifest.name} ${manifest.version}\n`)
  for (const file of files) licenses.push(await readFile(path.join(packageRoot, file), 'utf8'))
}
const licenseJson = JSON.stringify(licenses.join('\n')).replaceAll('<', '\\u003c')
html = html.replace('</body>', `<script type="application/json" id="preview-licenses">${licenseJson}</script></body>`)
if (/<script\b[^>]*\bsrc=|<link\b[^>]*rel="(?:stylesheet|modulepreload)"/.test(html)) {
  throw new Error('预览仍含外部脚本或样式。')
}
await mkdir(path.dirname(destination), { recursive: true })
await writeFile(destination, html, 'utf8')
console.log(`离线预览：${destination}\n大小：${(Buffer.byteLength(html) / 1024).toFixed(1)} KiB`)
