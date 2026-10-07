import { useEffect, useRef, useState } from 'react'
import type { Snapshot, UiGateway } from '@/data/types'
import { dolbyRunning, type DolbyOperation, type DolbyProfile } from '@/data/dolby'
import { OperationFailure } from '@/data/desktop'

export function useDolbyApply(gateway: UiGateway, id: string, onSaved: (s: Snapshot) => void, onClose: () => void, onFailure: (e: OperationFailure) => void, onSettled: (s: Snapshot) => void, onResult?: (text: string, error: boolean) => void) {
  const [job, setJob] = useState<DolbyOperation | null>(null), [starting, setStarting] = useState(false), [error, setError] = useState(''), [uncertain, setUncertain] = useState(false)
  const current = useRef<DolbyOperation | null>(null), alive = useRef(true), locked = useRef(false), closeWanted = useRef(false), cancelWanted = useRef(false)
  const callbacks = useRef({ onSaved, onClose, onFailure, onSettled, onResult }); callbacks.current = { onSaved, onClose, onFailure, onSettled, onResult }
  function accept(value: DolbyOperation) {
    if (value.Token !== current.current?.Token || value.DeviceId !== id) throw new Error('应用结果与当前任务不一致，请重新检查。')
    current.current = value
    if (!alive.current) return
    setJob(value)
    if (!dolbyRunning(value)) {
      const report = () => callbacks.current.onResult?.(value.Message, value.Status === 'error' || value.Status === 'warning')
      report()
      // One snapshot after completion; never use a global busy flag as proof of success.
      void gateway.read().then(s => { if (alive.current && current.current?.Token === value.Token) { callbacks.current.onSettled(s); report() } }).catch(() => {})
    }
    if (!dolbyRunning(value) && closeWanted.current) {
      // Keep failures visible in the main panel when closing the editor.
      if (!callbacks.current.onResult && value.Status !== 'applied') callbacks.current.onFailure(new OperationFailure(value.Message))
      callbacks.current.onClose()
    }
  }
  useEffect(() => { alive.current = true; return () => { alive.current = false; if (dolbyRunning(current.current)) void gateway.cancelDolbyApply(current.current!.Token).catch(() => {}) } }, [gateway])
  useEffect(() => {
    if (!dolbyRunning(job) || starting || error) return
    let active = true
    const timer = setTimeout(() => { gateway.readDolbyApply(job!.Token).then(v => { if (active) accept(v) }).catch(e => { if (active) setError(e instanceof Error ? e.message : '无法确认应用结果，请重试读取。') }) }, 250)
    return () => { active = false; clearTimeout(timer) }
  }, [job, starting, error, gateway])
  async function cancel(close = false) {
    closeWanted.current ||= close; cancelWanted.current = true
    if (locked.current) return
    if (!dolbyRunning(current.current)) { if (close) callbacks.current.onClose(); return }
    locked.current = true; setError('')
    try { accept(await gateway.cancelDolbyApply(current.current!.Token)) }
    catch (e) { if (alive.current) setError(e instanceof Error ? e.message : '尚未确认应用停止，请重试。') }
    finally { locked.current = false }
  }
  async function start(profile: DolbyProfile, expected: DolbyProfile | null) {
    if (locked.current || dolbyRunning(current.current) || uncertain) return
    locked.current = true; closeWanted.current = cancelWanted.current = false; setStarting(true); setError('')
    const token = crypto.randomUUID().replaceAll('-', '')
    current.current = { Token:token, DeviceId:id, Status:'running', Message:'正在保存并请求应用…' }; setJob(current.current)
    try {
      const result = await gateway.startDolbyApply(id, profile, expected, token)
      if (alive.current) callbacks.current.onSaved(result.snapshot)
      accept(result.operation)
      if (!alive.current || cancelWanted.current || closeWanted.current) accept(await gateway.cancelDolbyApply(token))
    } catch (e) {
      if (!alive.current) { void gateway.cancelDolbyApply(token).catch(() => {}); return }
      const failure = e instanceof OperationFailure ? e : new OperationFailure(e instanceof Error ? e.message : '无法确认应用结果。', undefined, true)
      setError(failure.message); callbacks.current.onFailure(failure)
      if (failure.requiresRefresh) setUncertain(true)
      else { current.current = null; setJob(null); if (closeWanted.current) callbacks.current.onClose() }
    } finally { locked.current = false; if (alive.current) setStarting(false) }
  }
  return { job, starting, error, uncertain, busy:starting || dolbyRunning(job), start, cancel, retry:() => setError('') }
}
