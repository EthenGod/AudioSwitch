import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import { PromptApp } from './PromptApp'
import { createPreviewPromptGateway } from './data/prompt'
import './styles.css'

// The offline preview always uses memory data, even inside a desktop webview.
const gateway = createMockGateway(260, ['书桌 USB 耳机'])
const query = new URLSearchParams(window.location.search)
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{(query.get('view') ?? document.documentElement.dataset.previewView) === 'prompt'
    ? <div className="prompt-preview-stage"><PromptApp gateway={createPreviewPromptGateway(query.has('disconnected') || document.documentElement.dataset.previewDisconnected === 'true', !query.has('light'))} /></div>
    : <App gateway={gateway} />}</React.StrictMode>,
)
