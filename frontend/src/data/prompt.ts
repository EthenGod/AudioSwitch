import { invoke } from '@tauri-apps/api/core'
import { mapSnapshot } from './desktop'
import { createMockGateway } from './mock'
import type { Flow, Snapshot } from './types'

export interface Arrival {
  Token: string; Flow: Flow; NewIds: string[]; Disconnected: boolean
  PreviousName: string; PreviousDefaults: Snapshot['Defaults']
}
export interface PromptState { snapshot: Snapshot; pending: Arrival[] }
export type PromptAction = { kind: 'selected'; token: string; id: string } | { kind: 'previous' | 'current'; token: string }
export interface PromptGateway {
  mode: 'preview' | 'desktop'
  read(): Promise<PromptState>
  respond(action: PromptAction): Promise<PromptState>
  close(): Promise<void>
  openPanel(): Promise<void>
}
export class PromptFailure extends Error {
  constructor(message: string, public state?: PromptState) { super(message) }
}
export function mapPrompt(value: unknown): PromptState {
  const snapshot = mapSnapshot(value), reply = value as Record<string, unknown>
  const invalid = () => new Error('设备提示不完整，请刷新后重试。')
  if (!Array.isArray(reply.Pending)) throw invalid()
  const tokens = new Set<string>()
  const pending = reply.Pending.map((raw): Arrival => {
    if (!raw || typeof raw !== 'object') throw invalid()
    const p = raw as Record<string, unknown>
    if (typeof p.Token !== 'string' || !p.Token || tokens.has(p.Token) || (p.Flow !== 0 && p.Flow !== 1) || !Array.isArray(p.NewDevices) || !Array.isArray(p.DisconnectedDevices) || typeof p.PreviousName !== 'string' || !p.PreviousDefaults || typeof p.PreviousDefaults !== 'object' || Array.isArray(p.PreviousDefaults)) throw invalid()
    tokens.add(p.Token)
    const ids = (rows: unknown[]) => rows.map(raw => {
      if (!raw || typeof raw !== 'object' || !('Id' in raw) || typeof raw.Id !== 'string' || !raw.Id || !('Flow' in raw) || raw.Flow !== p.Flow) throw invalid()
      return raw.Id
    })
    const previous: Snapshot['Defaults'] = {}
    for (const [key, id] of Object.entries(p.PreviousDefaults)) {
      if (!/^[01]:[012]$/.test(key) || (id !== null && typeof id !== 'string')) throw invalid()
      if (typeof id === 'string' && id) previous[key as keyof typeof previous] = id
    }
    return { Token: p.Token, Flow: p.Flow, NewIds: ids(p.NewDevices), Disconnected: ids(p.DisconnectedDevices).length > 0, PreviousName: p.PreviousName, PreviousDefaults: previous }
  })
  return { snapshot, pending }
}
export function canRestore(state: PromptState, arrival: Arrival): boolean {
  if (arrival.Disconnected) return false
  const roles = state.snapshot.Preferences.IncludeCommunications ? [0, 1, 2] as const : [0, 1] as const
  const ids = roles.map(role => arrival.PreviousDefaults[`${arrival.Flow}:${role}`]).filter((id): id is string => !!id)
  return ids.length > 0 && ids.every(id => state.snapshot.Devices.some(d => d.Id === id && d.Flow === arrival.Flow && d.Online))
}
type Transport = (command: string, args?: Record<string, unknown>) => Promise<unknown>
function message(error: unknown): string { return error && typeof error === 'object' && 'message' in error ? String(error.message) : String(error) }
export function createPromptGateway(call: Transport = invoke): PromptGateway {
  let busy = false, uncertain = false
  return {
    mode: 'desktop',
    async read() {
      if (busy) throw new Error('正在处理上一个操作。')
      try { const state = mapPrompt(await call('read_prompt_snapshot')); uncertain = false; return state }
      catch (error) { throw new PromptFailure(message(error)) }
    },
    async respond(action) {
      if (busy || uncertain) throw new Error('请先刷新确认上次结果，勿重复提交。')
      busy = true
      try {
        const raw = await call('prompt_action', { action })
        const state = mapPrompt(raw), reply = raw as Record<string, unknown>
        if (!Object.hasOwn(reply, 'OperationError')) throw new Error('后台没有确认操作结果。')
        if (reply.OperationError) throw new PromptFailure(String(reply.OperationError), state)
        if (state.pending.some(p => p.Token === action.token)) throw new Error('提示尚未处理，请刷新确认。')
        return state
      } catch (error) {
        uncertain = true
        if (error instanceof PromptFailure) throw error
        throw new PromptFailure(`${message(error)} 请刷新确认后再操作。`)
      } finally { busy = false }
    },
    async close() { await call('close_prompt') },
    async openPanel() { await call('open_prompt_panel') },
  }
}
export function createPreviewPromptGateway(disconnected = false, dark = true): PromptGateway {
  const mock = createMockGateway()
  let state: PromptState | undefined
  return {
    mode: 'preview',
    async read() {
      if (!state) {
        const snapshot = await mock.read(), device = snapshot.Devices.find(d => d.Online && d.Flow === 0)!
        snapshot.Preferences.DarkMode = dark
        state = { snapshot: { ...snapshot, CanWrite: true }, pending: [{ Token: 'preview-output', Flow: 0, NewIds: [device.Id], Disconnected: disconnected, PreviousName: '桌面扬声器', PreviousDefaults: { ...snapshot.Defaults } }] }
      }
      return structuredClone(state)
    },
    async respond(action) {
      const current = await this.read()
      const arrival = current.pending.find(p => p.Token === action.token)
      if (!arrival) throw new Error('提示已失效，请刷新。')
      const roles = current.snapshot.Preferences.IncludeCommunications ? [0, 1, 2] as const : [0, 1] as const
      for (const role of roles) {
        const key = `${arrival.Flow}:${role}` as const
        if (action.kind === 'selected') current.snapshot.Defaults[key] = action.id
        if (action.kind === 'previous' && arrival.PreviousDefaults[key]) current.snapshot.Defaults[key] = arrival.PreviousDefaults[key]
      }
      current.pending = current.pending.filter(p => p.Token !== action.token); state = current
      return structuredClone(state)
    },
    async close() {},
    async openPanel() { window.location.assign(window.location.pathname) },
  }
}
