import { useEffect, useState } from 'react'
import { App } from './App'
import { browserToken, createBrowserConnection } from './data/browser'

export function openBrowserPanel() {
  try {
    const token = browserToken(window.location, sessionStorage)
    history.replaceState(null, '', '/?browser')
    const connection = createBrowserConnection(token)
    window.addEventListener('pagehide', () => connection.close())
    window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload() })
    return <BrowserPanel connection={connection} />
  } catch (error) { return <ConnectionEnded message={error instanceof Error ? error.message : '无法打开浏览器面板。'} /> }
}
function ConnectionEnded({ message }: { message: string }) {
  return <main className="error-state" role="alert"><h1>连接已结束</h1><p>{message}</p></main>
}
function BrowserPanel({ connection }: { connection: ReturnType<typeof createBrowserConnection> }) {
  const [ended, setEnded] = useState(false)
  useEffect(() => connection.subscribe(() => setEnded(true)), [connection])
  return ended ? <ConnectionEnded message="请双击托盘图标重新打开面板。" /> : <App gateway={connection.gateway} />
}
