import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { App } from './App'
import { createMockGateway } from './data/mock'
import type { Snapshot } from './data/types'

it('keeps settings mounted and visually available during a write without accepting duplicate changes', async () => {
  const gateway = createMockGateway(0), before = await gateway.read()
  let finish!: (data: Snapshot) => void
  const save = vi.spyOn(gateway, 'setPreference').mockImplementation(() => new Promise(resolve => { finish = resolve }))
  const user = userEvent.setup(); render(<App gateway={gateway} />)
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: '应用设置' }))
  const game = screen.getByRole('switch', { name: '游戏模式' })
  const update = screen.getByRole('switch', { name: '自动更新' })
  const port = screen.getByRole('textbox', { name: '端口号' })
  await user.click(game)
  expect(game).toHaveAttribute('data-pending', 'true')
  expect(update).toHaveAttribute('data-pending', 'true')
  expect(game).not.toBeChecked()
  expect(port).toBeEnabled(); expect(port).toHaveAttribute('readonly')
  fireEvent.click(game); fireEvent.click(update)
  expect(save).toHaveBeenCalledTimes(1)
  await act(async () => finish({ ...before, Preferences: { ...before.Preferences, GameMode: true } }))
  await waitFor(() => expect(game).toBeChecked())
  expect(screen.getByRole('switch', { name: '自动更新' })).toBe(update)
  expect(update).not.toHaveAttribute('data-pending')
  expect(update).toHaveAttribute('aria-disabled', 'true')
  expect(update.closest('.setting-row')).toHaveAttribute('data-muted', 'true')
  expect(port).not.toHaveAttribute('readonly')
})

it('pauses the update control without losing its preference and restores it when game mode ends', async () => {
  const gateway = createMockGateway(0), save = vi.spyOn(gateway, 'setPreference')
  const user = userEvent.setup(); render(<App gateway={gateway} />)
  await screen.findByRole('table'); await user.click(screen.getByRole('button', { name: '应用设置' }))
  expect(screen.getByText('@EthenGod')).toBeVisible()
  const game = screen.getByRole('switch', { name: '游戏模式' }), update = screen.getByRole('switch', { name: '自动更新' })
  await user.click(game); await waitFor(() => expect(game).toBeChecked())
  expect(update).toBeChecked(); expect(update).toHaveAttribute('aria-disabled', 'true')
  await user.click(update); expect(save).toHaveBeenCalledTimes(1)
  await user.click(game); await waitFor(() => expect(game).not.toBeChecked())
  expect(update).toBeChecked(); expect(update).not.toHaveAttribute('aria-disabled', 'true')
  expect(update.closest('.setting-row')).not.toHaveAttribute('data-muted')
  await user.click(update); await waitFor(() => expect(update).not.toBeChecked())
  await user.click(game); await waitFor(() => expect(game).toBeChecked())
  await user.click(game); await waitFor(() => expect(game).not.toBeChecked())
  expect(update).not.toBeChecked(); expect(update).not.toHaveAttribute('aria-disabled', 'true')
})
