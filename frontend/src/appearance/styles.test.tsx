import { act, render, renderHook, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { App } from '../App'
import { useAppearance } from '../useAppearance'
import { createMockGateway } from '../data/mock'
import { createDesktopGateway, mapSnapshot } from '../data/desktop'
import { backendFixture } from '../test/backendFixture'
import { DEFAULT_UI_STYLE, getUiStyle, isUiStyleId, UI_STYLES } from './styles'

// Exercise adding a style without shipping a second visual or demo preference.
const catalogue = UI_STYLES as { id: string; label: string }[]
const productionCount = catalogue.length
afterEach(() => { catalogue.splice(productionCount) })
const registerFixture = () => catalogue.push({ id: 'fixture-style', label: '测试风格' })
function reply(style = DEFAULT_UI_STYLE) {
  const raw = backendFixture()
  return { ...raw, PanelApiVersion: 13, PreferencesSaved: true, OperationError: null,
    Preferences: { ...raw.Preferences, UiStyle: style } }
}

it('has only the current style and rejects malformed IDs across the snapshot boundary', () => {
  expect(UI_STYLES).toEqual([{ id: 'classic', label: '经典' }])
  for (const value of ['', null, 1, true, 'A', '-style', 'a'.repeat(33), 'a\n', '../classic', 'a b', '深色']) {
    expect(isUiStyleId(value)).toBe(false)
    expect(() => mapSnapshot({ ...reply(), Preferences: { ...reply().Preferences, UiStyle: value } })).toThrow()
  }
  expect(isUiStyleId('a'.repeat(32))).toBe(true)
})

it('defaults old snapshots and preserves future IDs while falling back only for presentation', () => {
  const old = mapSnapshot(backendFixture())
  expect(old.Preferences.UiStyle).toBe('classic'); expect(old.CanSetUiStyle).toBe(false)
  const future = mapSnapshot(reply('future-style-2'))
  expect(future.Preferences.UiStyle).toBe('future-style-2')
  expect(getUiStyle(future.Preferences.UiStyle).id).toBe('classic')
  const { rerender } = renderHook(({ style, dark }) => useAppearance(dark, style), { initialProps: { style: 'future-style-2', dark: false } })
  expect(document.documentElement).toHaveAttribute('data-ui-style', 'classic')
  expect(document.documentElement).not.toHaveClass('dark')
  registerFixture(); rerender({ style: 'fixture-style', dark: false })
  expect(document.documentElement).toHaveAttribute('data-ui-style', 'fixture-style')
  expect(document.documentElement).not.toHaveClass('dark')
  rerender({ style: 'fixture-style', dark: true })
  expect(document.documentElement).toHaveClass('dark')
  expect(document.documentElement).toHaveAttribute('data-ui-style', 'fixture-style')
})

it('always offers the shared selector without saving an unchanged choice', async () => {
  const gateway = createMockGateway(0), save = vi.spyOn(gateway, 'setUiStyle')
  const user = userEvent.setup(); render(<App gateway={gateway} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  await user.click(screen.getByRole('combobox', { name: '界面风格' }))
  expect(screen.getAllByRole('option')).toHaveLength(1)
  await user.click(screen.getByRole('option', { name: '经典' }))
  await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument())
  expect(save).not.toHaveBeenCalled()
})

it('lets a user explicitly replace an unavailable saved style with classic', async () => {
  const call = vi.fn().mockResolvedValue(reply())
  const gateway = createDesktopGateway(async () => reply('unavailable-style'), call)
  const user = userEvent.setup(); render(<App gateway={gateway} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  await user.click(screen.getByRole('combobox', { name: '界面风格' }))
  await user.click(await screen.findByRole('option', { name: '经典' }))
  await waitFor(() => expect(call).toHaveBeenCalledExactlyOnceWith('panel_action', { action: { kind: 'uiStyle', style: 'classic' } }))
})

it('adds registered options, preserves controls and never changes audio when a style is saved', async () => {
  registerFixture()
  const gateway = createMockGateway(0), before = await gateway.read()
  const save = vi.spyOn(gateway, 'setUiStyle')
  const user = userEvent.setup(); const view = render(<App gateway={gateway} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  const port = screen.getByRole('textbox', { name: '端口号' })
  await user.type(port, 'draft')
  await user.click(screen.getByRole('combobox', { name: '界面风格' }))
  await user.click(await screen.findByRole('option', { name: '测试风格' }))
  await waitFor(() => expect(document.documentElement).toHaveAttribute('data-ui-style', 'fixture-style'))
  expect(save).toHaveBeenCalledExactlyOnceWith('fixture-style')
  expect(screen.getByRole('textbox', { name: '端口号' })).toBe(port)
  expect(port).toHaveValue('draft')
  const after = await gateway.read()
  expect(after).toEqual({ ...before, Preferences: { ...before.Preferences, UiStyle: 'fixture-style' } })
  view.unmount(); render(<App gateway={gateway} />)
  await screen.findByRole('table')
  expect(document.documentElement).toHaveAttribute('data-ui-style', 'fixture-style')
  expect((await gateway.setScenario('empty')).Preferences.UiStyle).toBe('fixture-style')
})

it('keeps the confirmed style during a pending save and restores it on failure', async () => {
  registerFixture()
  const gateway = createMockGateway(0)
  let reject!: (error: Error) => void
  const save = vi.spyOn(gateway, 'setUiStyle').mockImplementation(() => new Promise((_, no) => { reject = no }))
  const user = userEvent.setup(); render(<App gateway={gateway} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  await user.click(screen.getByRole('combobox', { name: '界面风格' }))
  await user.click(await screen.findByRole('option', { name: '测试风格' }))
  const selector = screen.getByRole('combobox', { name: '界面风格' })
  expect(selector).toHaveTextContent('经典'); expect(selector).toHaveAttribute('data-pending')
  await user.click(selector)
  expect(save).toHaveBeenCalledTimes(1)
  await act(async () => reject(new Error('无法保存风格')))
  await screen.findByText('无法保存风格')
  expect(selector).toHaveTextContent('经典')
  expect(document.documentElement).toHaveAttribute('data-ui-style', 'classic')
})

it('gates old backends and confirms persistence instead of accepting unchanged or unsaved replies', async () => {
  registerFixture()
  const call = vi.fn().mockResolvedValue(reply())
  const gateway = createDesktopGateway(async () => reply(), call)
  await gateway.read()
  await expect(gateway.setUiStyle('fixture-style')).rejects.toThrow('不一致')
  call.mockResolvedValue({ ...reply('fixture-style'), PreferencesSaved: false })
  await expect(gateway.setUiStyle('fixture-style')).rejects.toThrow('不一致')
  call.mockResolvedValue(reply('fixture-style'))
  await expect(gateway.setUiStyle('fixture-style')).resolves.toHaveProperty('Preferences.UiStyle', 'fixture-style')
  expect(call).toHaveBeenLastCalledWith('panel_action', { action: { kind: 'uiStyle', style: 'fixture-style' } })
  await expect(gateway.setUiStyle('not-installed')).rejects.toThrow('不支持')
  expect(call).toHaveBeenCalledTimes(3)
  const old = createDesktopGateway(async () => ({ ...reply(), PanelApiVersion: 12 }), call)
  await old.read()
  await expect(old.setUiStyle('fixture-style')).rejects.toThrow('新版后台')
  expect(call).toHaveBeenCalledTimes(3)
})
