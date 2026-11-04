import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { App } from './App'
import { openBrowserPanel } from './BrowserPanel'
import { createDesktopGateway } from './data/desktop'
import { backendFixture } from './test/backendFixture'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

it('tracks the system appearance on a dead browser link, independent of the previous page', () => {
  let light = true
  const events = new EventTarget()
  const media = Object.defineProperty(events, 'matches', { get: () => light }) as MediaQueryList
  vi.spyOn(window, 'matchMedia').mockReturnValue(media)
  document.documentElement.classList.add('dark')
  render(openBrowserPanel())
  expect(screen.getByRole('alert')).toHaveTextContent('连接已结束')
  expect(document.documentElement.style.colorScheme).toBe('light')
  light = false
  act(() => { events.dispatchEvent(new Event('change')) })
  expect(document.documentElement.style.colorScheme).toBe('dark')
})

it('uses dark for a standalone error when system appearance is unavailable', () => {
  vi.stubGlobal('matchMedia', undefined)
  render(openBrowserPanel())
  expect(screen.getByRole('alert')).toBeVisible()
  expect(document.documentElement.style.colorScheme).toBe('dark')
})

it('returns to the panel default on reconnect, even on a light system', async () => {
  const original = window.matchMedia
  vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ ...original(query), matches: query === '(prefers-color-scheme: light)' }))
  const reply = backendFixture()
  const { DarkMode: _theme, ...preferences } = reply.Preferences
  const read = vi.fn().mockRejectedValueOnce('后台未运行').mockResolvedValue({ ...reply, Preferences: preferences })
  const ready = vi.fn()
  render(<App gateway={createDesktopGateway(read)} onReady={ready} />)
  await screen.findByText('后台未运行')
  await waitFor(() => expect(ready).toHaveBeenCalledWith(false, true))
  expect(screen.queryByText('连接中断，仅显示上次状态')).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: '重试' }))
  await screen.findByRole('table')
  await waitFor(() => expect(ready).toHaveBeenLastCalledWith(true))
  expect(document.documentElement.style.colorScheme).toBe('dark')
})
