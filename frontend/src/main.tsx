import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import { invoke, isTauri } from '@tauri-apps/api/core'
import { createDesktopGateway } from './data/desktop'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { PromptApp } from './PromptApp'
import { createPreviewPromptGateway, createPromptGateway } from './data/prompt'
import './styles.css'
import { openBrowserPanel } from './BrowserPanel'

function panel() {
  if (isTauri()) {
    if (getCurrentWindow().label === 'prompt') return <PromptApp gateway={createPromptGateway()} />
    return <App gateway={createDesktopGateway()} onReady={dark => { void invoke('panel_ready', { dark }).catch(console.error) }} />
  }
  const query = new URLSearchParams(window.location.search)
  if (import.meta.env.MODE === 'desktop' || query.has('browser')) return openBrowserPanel()
  return query.get('view') === 'prompt'
    ? <PromptApp gateway={createPreviewPromptGateway(query.has('disconnected'), !query.has('light'))} />
    : <App gateway={createMockGateway()} />
}
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{panel()}</React.StrictMode>,
)
