import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach } from 'vitest'
import { setLanguage } from '../i18n'

afterEach(cleanup)
beforeEach(() => {
  Object.defineProperty(navigator, 'languages', { configurable: true, value: ['zh-CN'] })
  setLanguage('zh-CN')
})
window.matchMedia = query => ({ matches: false, media: query, onchange: null, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent: () => true })
globalThis.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} }
