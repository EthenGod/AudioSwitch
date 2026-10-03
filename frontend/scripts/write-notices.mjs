import { readFile, readdir, mkdir, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.argv[2] === 'dist-desktop' ? 'dist-desktop' : 'dist'
const lock = JSON.parse(await readFile(path.join(root, 'package-lock.json'), 'utf8'))
const sections = [await readFile(path.join(root, 'THIRD-PARTY.md'), 'utf8')]
let count = 0
for (const [directory, entry] of Object.entries(lock.packages)) {
  if (!directory || entry.dev || entry.devOptional) continue
  const packageRoot = path.join(root, directory)
  const manifest = JSON.parse(await readFile(path.join(packageRoot, 'package.json'), 'utf8'))
  const files = (await readdir(packageRoot)).filter(name => /^(licen[sc]e|copying)(\..*)?$/i.test(name))
  if (!files.length) throw new Error(`Missing license: ${manifest.name}`)
  sections.push(`\n---\n${manifest.name} ${manifest.version}\n${manifest.license ?? entry.license ?? ''}\n`)
  for (const file of files) sections.push(await readFile(path.join(packageRoot, file), 'utf8'))
  count++
}
await mkdir(path.join(root, output), { recursive: true })
await writeFile(path.join(root, output, 'THIRD-PARTY-NOTICES.txt'), sections.join('\n'), 'utf8')
console.log(`Included license texts for ${count} runtime packages and shadcn/ui.`)
