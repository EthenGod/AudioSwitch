import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, it } from 'vitest'

const read = (name: string) => readFileSync(resolve('src-tauri', name), 'utf8')

it('registers every native command in the application ACL and an intended window capability', () => {
  const handlers = read('src/main.rs').match(/generate_handler!\[([^\]]+)\]/)![1].split(',').map(value => value.trim())
  const manifest = read('build.rs').match(/\.commands\(&\[([^\]]+)\]/)![1].match(/"([a-z_]+)"/g)!.map(value => JSON.parse(value) as string)
  const panel = JSON.parse(read('capabilities/read-only.json'))
  const prompt = JSON.parse(read('capabilities/prompt.json'))
  expect([...manifest].sort()).toEqual([...handlers].sort())
  for (const command of handlers) {
    const permission = `allow-${command.replaceAll('_', '-')}`
    expect([...panel.permissions, ...prompt.permissions], command).toContain(permission)
    if (command.includes('upgrade')) {
      expect(panel.permissions).toContain(permission)
      expect(prompt.permissions).not.toContain(permission)
    }
  }
})
