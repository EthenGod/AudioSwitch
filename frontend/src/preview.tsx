import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import { PromptApp } from './PromptApp'
import { createPreviewPromptGateway } from './data/prompt'
import './styles.css'

// The offline preview always uses memory data, even inside a desktop webview.
const gateway = createMockGateway()
const query = new URLSearchParams(window.location.search)
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{query.get('view') === 'prompt'
    ? <PromptApp gateway={createPreviewPromptGateway(query.has('disconnected'), !query.has('light'))} />
    : <App gateway={gateway} />}</React.StrictMode>,
)
