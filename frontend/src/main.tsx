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

const desktop = isTauri(), query = new URLSearchParams(window.location.search)
const prompt = desktop ? getCurrentWindow().label === 'prompt' : query.get('view') === 'prompt'
const panelReady = desktop ? (dark: boolean) => { void invoke('panel_ready', { dark }).catch(console.error) } : undefined
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{!desktop && query.has('browser') ? openBrowserPanel() : prompt ? <PromptApp gateway={desktop ? createPromptGateway() : createPreviewPromptGateway(query.has('disconnected'), !query.has('light'))} /> : <App gateway={desktop ? createDesktopGateway() : createMockGateway()} onReady={panelReady} />}</React.StrictMode>,
)
