import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { createMockGateway } from './data/mock'
import { isTauri } from '@tauri-apps/api/core'
import { createDesktopGateway } from './data/desktop'
import './styles.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode><App gateway={isTauri() ? createDesktopGateway() : createMockGateway()} /></React.StrictMode>,
)
