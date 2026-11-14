import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import { PromptApp } from './PromptApp'
import { createPreviewPromptGateway } from './data/prompt'
import { resolveLanguage, setLanguage } from './i18n'
import './styles.css'

// The offline preview always uses memory data, even inside a desktop webview.
const query = new URLSearchParams(window.location.search)
const language = query.get('lang') ?? 'zh-CN'
setLanguage(resolveLanguage(query.has('first-run') ? null : language))
const gateway = createMockGateway(260, [], query.has('first-run') ? null : language)
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{(query.get('view') ?? document.documentElement.dataset.previewView) === 'prompt'
    ? <div className="prompt-preview-stage"><PromptApp gateway={createPreviewPromptGateway(query.has('disconnected') || document.documentElement.dataset.previewDisconnected === 'true', !query.has('light'), language)} /></div>
    : <App gateway={gateway} />}</React.StrictMode>,
)
