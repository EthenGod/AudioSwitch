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
  expect(port).not.toHaveAttribute('readonly')
})
