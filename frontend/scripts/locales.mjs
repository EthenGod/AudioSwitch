// Shared catalogue checks and community scaffolding. No application/config access.
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../../', import.meta.url))
const directory = path.join(root, 'locales')
const read = name => JSON.parse(fs.readFileSync(path.join(directory, `${name}.json`), 'utf8'))
const write = (name, data) => fs.writeFileSync(path.join(directory, `${name}.json`), JSON.stringify(data, null, 2) + '\n')
const languages = read('languages'), base = read('zh-CN')
const validId = id => typeof id === 'string' && /^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/.test(id) && id.length <= 35
const parameters = text => [...new Set([...text.matchAll(/\{(\d+)\}/g)].map(match => match[1]))].sort().join(',')

if (process.argv[2] === 'add') {
  const [, , , id, name] = process.argv
  if (!validId(id) || !name || name.length > 80 || languages.some(item => item.id.toLowerCase() === id.toLowerCase())) throw Error('Usage: npm run locale:add -- fr Français (new language ID and native name)')
  if (fs.existsSync(path.join(directory, `${id}.json`))) throw Error('Catalogue already exists; no file was overwritten')
  write(id, Object.fromEntries(Object.keys(base).map(key => [key, ''])))
  write('languages', [...languages, { id, name, match: [id.toLowerCase()] }])
  console.log(`Created locales/${id}.json. Empty entries fall back to Chinese while translating. Run npm run locale:check before submitting.`)
} else {
  const errors = [], seen = new Set(), prefixes = new Set()
  for (const item of languages) {
    if (!validId(item.id) || typeof item.name !== 'string' || !item.name.trim() || !Array.isArray(item.match) || item.match.some(tag => !validId(tag) || tag !== tag.toLowerCase())) { errors.push('Invalid language metadata: ' + JSON.stringify(item)); continue }
    if (seen.has(item.id.toLowerCase())) errors.push('Duplicate language ID: ' + item.id)
    seen.add(item.id.toLowerCase())
    for (const prefix of item.match) {
      if (prefixes.has(prefix)) errors.push('Ambiguous system language prefix: ' + prefix)
      prefixes.add(prefix)
    }
    const raw = fs.readFileSync(path.join(directory, `${item.id}.json`), 'utf8'), catalogue = JSON.parse(raw)
    const keys = [...raw.matchAll(/^\s*("(?:[^"\\]|\\.)*")\s*:/gm)].map(match => JSON.parse(match[1]))
    if (new Set(keys).size !== keys.length) errors.push(item.id + ': duplicate message keys')
    for (const [key, source] of Object.entries(base)) {
      const value = catalogue[key]
      if (typeof value !== 'string' || !value.trim()) errors.push(`${item.id}: missing ${JSON.stringify(key)}`)
      else if (parameters(value) !== parameters(source)) errors.push(`${item.id}: placeholders differ for ${JSON.stringify(key)}`)
    }
    for (const key of Object.keys(catalogue)) if (!Object.hasOwn(base, key)) errors.push(`${item.id}: unknown key ${JSON.stringify(key)}`)
  }
  if (!seen.has('zh-cn')) errors.push('The Chinese fallback is required')
  // Check literal message references without scanning dependencies or outputs.
  function visit(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const file = path.join(directory, entry.name)
      if (entry.isDirectory()) { if (!['test', 'i18n'].includes(entry.name)) visit(file); continue }
      if (!/\.(tsx?|cs|rs)$/.test(file) || /\.(test|spec)\./.test(file)) continue
      const source = fs.readFileSync(file, 'utf8')
      for (const match of source.matchAll(/(?:\bt|L\.Text|localization::(?:text|format))\(\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')/g)) {
        const literal = match[1]
        const key = literal[0] === '"' ? JSON.parse(literal) : literal.slice(1, -1).replace(/\\'/g, "'")
        if (!Object.hasOwn(base, key)) errors.push(`${path.relative(root, file)}: uncatalogued ${JSON.stringify(key)}`)
      }
    }
  }
  for (const directory of ['src', 'frontend/src', 'frontend/src-tauri/src']) visit(path.join(root, directory))
  if (errors.length) { console.error([...new Set(errors)].join('\n')); process.exitCode = 1 }
  else console.log(`${languages.length} languages, ${Object.keys(base).length} messages: keys and placeholders verified`)
}
