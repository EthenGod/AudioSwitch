import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import './styles.css'

// The offline preview always uses memory data, even inside a desktop webview.
const gateway = createMockGateway()
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode><App gateway={gateway} /></React.StrictMode>,
)
