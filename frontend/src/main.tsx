import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import { isTauri } from '@tauri-apps/api/core'
import { createDesktopGateway } from './data/desktop'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { PromptApp } from './PromptApp'
import { createPreviewPromptGateway, createPromptGateway } from './data/prompt'
import './styles.css'

const desktop = isTauri(), query = new URLSearchParams(window.location.search)
const prompt = desktop ? getCurrentWindow().label === 'prompt' : query.get('view') === 'prompt'
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{prompt ? <PromptApp gateway={desktop ? createPromptGateway() : createPreviewPromptGateway(query.has('disconnected'), !query.has('light'))} /> : <App gateway={desktop ? createDesktopGateway() : createMockGateway()} />}</React.StrictMode>,
)
