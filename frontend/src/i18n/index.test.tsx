import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { createMockGateway } from '../data/mock'
import { createDesktopGateway, mapSnapshot } from '../data/desktop'
import { backendFixture } from '../test/backendFixture'
import { formatList, matchLanguage, resolveLanguage, translate, setLanguage, t } from './index'
import { FileCheckResult } from '../components/FileCheckResult'
import { createBrowserConnection } from '../data/browser'
import { useLanguage } from './useLanguage'

describe('language selection', () => {
  it('commits translated DOM inside a captured update without remounting input or stealing focus', async () => {
    let commit!: () => void, finish!: () => void
    const finished = new Promise<void>(resolve => { finish = resolve })
    Object.defineProperty(document, 'startViewTransition', { configurable: true, value: (update: () => void) => {
      commit = update
      return { ready: Promise.resolve(), finished, skipTransition: vi.fn() }
    } })
    function Surface({ language }: { language: string }) {
      useLanguage(language)
      return <section><h1>{t('应用设置')}</h1><input aria-label={t('搜索设备')} defaultValue="Draft" /></section>
    }
    const view = render(<Surface language="zh-CN" />)
    try {
      const input = screen.getByRole('textbox'); input.focus()
      view.rerender(<Surface language="en" />)
      expect(screen.getByRole('heading')).toHaveTextContent('应用设置')
      act(() => commit())
      expect(screen.getByRole('heading')).toHaveTextContent('Settings')
      expect(screen.getByRole('textbox', { name: 'Search devices' })).toBe(input)
      expect(input).toHaveValue('Draft'); expect(input).toHaveFocus()
      await act(async () => finish())
    } finally { view.unmount(); Reflect.deleteProperty(document, 'startViewTransition') }
  })
  it('formats multiple device names using the current language without translating the names', () => {
    setLanguage('en')
    expect(formatList(['书桌', 'USB Mic'])).toBe('书桌 & USB Mic')
    setLanguage('ja')
    expect(formatList(['书桌', 'USB Mic'])).toBe('书桌、USB Mic')
    setLanguage('zh-CN')
  })
  it('matches ordered system preferences and regional tags, with Chinese fallback', () => {
    expect(matchLanguage(['ja-JP'])).toBe('ja')
    expect(matchLanguage(['zz-ZZ', 'en-GB'])).toBe('en')
    expect(matchLanguage(['zh-TW'])).toBe('zh-CN')
    expect(matchLanguage(['zz-ZZ'])).toBe('zh-CN')
    expect(resolveLanguage('unknown', 'en-US')).toBe('zh-CN')
    expect(resolveLanguage(null, 'en-US')).toBe('en')
    expect(translate('unknown', '关闭开关')).toBe('关闭')
    expect(translate('en', '设置 {0}，{1}', 'Name {1}', 'USB')).toContain('Name {1}')
  })
  it('recommends the system language, confirms once, and keeps the choice on reload', async () => {
    Object.defineProperty(navigator, 'languages', { configurable: true, value: ['ja-JP'] })
    const gateway = createMockGateway(0, [], null), user = userEvent.setup()
    const save = vi.spyOn(gateway, 'setLanguage')
    const view = render(<App gateway={gateway} />)
    expect(await screen.findByRole('radio', { name: '日本語' })).toBeChecked()
    await user.click(screen.getByRole('radio', { name: 'English' }))
    await user.click(screen.getByRole('button', { name: translate('en', '确认') }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(save).toHaveBeenCalledExactlyOnceWith('en')
    expect((await gateway.read()).Preferences.Language).toBe('en')
    view.unmount(); render(<App gateway={gateway} />)
    await screen.findByRole('table')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(document.documentElement.lang).toBe('en')
  })
  it('keeps the chooser open and the saved language unset after a failed save', async () => {
    const gateway = createMockGateway(0, [], null), user = userEvent.setup()
    vi.spyOn(gateway, 'setLanguage').mockRejectedValue(new Error('Failed'))
    render(<App gateway={gateway} />)
    await screen.findByRole('dialog')
    await user.click(screen.getByRole('radio', { name: 'English' }))
    await user.click(screen.getByRole('button', { name: translate('en', '确认') }))
    await screen.findByText(translate('en', '语言未保存，请重试'))
    expect((await gateway.read()).Preferences.Language).toBeNull()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
  it('switches settings without resetting local search input or device names', async () => {
    const raw = backendFixture(), user = userEvent.setup()
    let reply = { ...raw, PanelApiVersion: 14, Preferences: { ...raw.Preferences, Language: 'zh-CN' } }
    const gateway = createDesktopGateway(async () => reply)
    render(<App gateway={gateway} />)
    const search = await screen.findByRole('textbox', { name: '搜索设备' })
    await user.type(search, '同名')
    // A confirmed snapshot from another panel updates the language in place.
    reply = { ...reply, Preferences: { ...reply.Preferences, Language: 'en' } }
    act(() => window.dispatchEvent(new Event('focus')))
    await waitFor(() => expect(document.documentElement.lang).toBe('en'))
    expect(screen.getByRole('textbox', { name: t('搜索设备') })).toBe(search)
    expect(search).toHaveValue('同名')
    expect((await gateway.read()).Devices.find(device => device.Id === 'endpoint-a')?.Name).toBe('同名扬声器')
  })
  it('does not infer integrity status from translated text', () => {
    setLanguage('en')
    render(<FileCheckResult status="repairable" message="Check finished" entries={['All good', 'Something missing']} entryStatuses={['passed', 'failed']} />)
    expect(screen.getByText('All good').closest('li')).toHaveClass('is-passed')
    expect(screen.getByText('Something missing').closest('li')).toHaveClass('is-failed')
    expect(screen.getByText('1 passed')).toBeInTheDocument()
  })
  it('uses the selected language for browser errors after the module was loaded', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 403 }))
    const connection = createBrowserConnection('test', request)
    setLanguage('en')
    await expect(connection.gateway.read()).rejects.toThrow(translate('en', '浏览器面板连接已结束，请双击托盘图标重新打开。'))
    connection.close()
  })
  it('gates the language command and requires confirmed persistence', async () => {
    const raw = backendFixture()
    expect(mapSnapshot(raw).CanSetLanguage).toBe(false)
    expect(mapSnapshot(raw).Preferences.Language).toBeNull()
    const reply = { ...raw, PanelApiVersion: 14, PreferencesSaved: true, OperationError: null, Preferences: { ...raw.Preferences, Language: 'zh-CN' } }
    const call = vi.fn().mockResolvedValue(reply)
    const gateway = createDesktopGateway(async () => reply, call)
    await gateway.read()
    await expect(gateway.setLanguage('en')).rejects.toThrow()
    call.mockResolvedValue({ ...reply, Preferences: { ...reply.Preferences, Language: 'en' } })
    await gateway.read()
    expect((await gateway.setLanguage('en')).Preferences.Language).toBe('en')
    expect(call).toHaveBeenLastCalledWith('panel_action', { action: { kind: 'language', language: 'en' } })
  })
})
