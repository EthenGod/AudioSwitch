import { createDesktopGateway, OperationFailure } from './desktop'

const disconnected = '浏览器面板连接已结束，请双击托盘图标重新打开。'

/** Session secret stays on this loopback origin, never in a URL query or a cookie. */
export function browserToken(location: Location, storage: Storage): string {
  if (location.hostname !== '127.0.0.1' || location.protocol !== 'http:') throw new Error('浏览器面板仅支持本机连接。')
  const incoming = location.hash.slice(1)
  const token = incoming || storage.getItem('audioswitch-session') || ''
  if (!/^[a-fA-F0-9]{64}$/.test(token)) throw new Error('此面板链接已失效，请双击托盘图标重新打开。')
  storage.setItem('audioswitch-session', token)
  return token
}

export function createBrowserConnection(token: string, request: typeof fetch = fetch) {
  const controller = new AbortController()
  let ended = false
  let session = ''
  const listeners = new Set<() => void>()
  const headers = { 'X-AudioSwitch-Token': token }
  const end = () => { if (!ended) { ended = true; listeners.forEach(listener => listener()) } }
  const ready = request('/session', { headers, signal: controller.signal, cache: 'no-store', credentials: 'omit', redirect: 'error' }).then(response => {
    if (!response.ok || !response.body) throw new Error(disconnected)
    session = response.headers.get('X-AudioSwitch-Session') || ''
    if (!/^[a-fA-F0-9]{32}$/.test(session)) throw new Error(disconnected)
    const reader = response.body.getReader()
    // A streaming connection survives background-tab timer throttling.
    void (async () => { try { while (!(await reader.read()).done) { /* server keepalive */ } } catch { /* connection ended */ } finally { reader.releaseLock(); end() } })()
  }).catch(error => { end(); throw error })
  void ready.catch(() => {}) // The gateway reports the initial failure in the panel.
  const call = async (command: string, args: Record<string, unknown>) => {
    try { await ready } catch { throw new OperationFailure(disconnected) }
    if (ended) throw new OperationFailure(disconnected)
    let response: Response
    try {
      response = await request('/rpc', { method: 'POST', headers: { ...headers, 'X-AudioSwitch-Session': session, 'Content-Type': 'application/json' }, body: JSON.stringify({ command, args }), cache: 'no-store', credentials: 'omit', redirect: 'error' })
      if (!response.ok) throw new Error(disconnected)
      const reply = await response.json() as { ok: boolean; value?: unknown; error?: unknown }
      if (reply.ok === true) return reply.value
      if (reply.ok === false && reply.error) throw reply.error
      throw new Error('后台响应不完整，请刷新确认。')
    } catch (error) {
      // A lost reply may follow a completed mutation. Never retry automatically.
      if (error instanceof TypeError) throw new OperationFailure(disconnected, undefined, true)
      throw error
    }
  }
  return {
    gateway: createDesktopGateway(() => call('read_snapshot', {}), call),
    subscribe(listener: () => void) { listeners.add(listener); if (ended) listener(); return () => { listeners.delete(listener) } },
    close() { controller.abort() },
  }
}
