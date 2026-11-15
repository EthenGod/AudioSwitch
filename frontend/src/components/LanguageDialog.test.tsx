import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { LanguageDialog } from './LanguageDialog'

it('keeps radio focus while captions change and confirms only the latest keyboard selection', async () => {
  const confirm = vi.fn(async () => true), user = userEvent.setup()
  render(<LanguageDialog recommended="ja" onConfirm={confirm} />)
  const system = await screen.findByRole('radio', { name: 'システムに合わせる' })
  expect(system).toBeChecked()
  const english = screen.getByRole('radio', { name: 'English' })
  await user.click(english)
  expect(english).toHaveFocus()
  expect(screen.getByRole('button', { name: 'Confirm' })).toBeInTheDocument()
  fireEvent.keyDown(english, { key: 'ArrowDown' })
  const japanese = screen.getByRole('radio', { name: '日本語' })
  expect(japanese).toHaveFocus(); expect(japanese).toBeChecked()
  fireEvent.keyDown(japanese, { key: 'Home' })
  expect(system).toHaveFocus(); expect(system).toBeChecked()
  expect(screen.getAllByRole('radio')).toHaveLength(4)
  await user.click(screen.getByRole('button', { name: '確認' }))
  await waitFor(() => expect(confirm).toHaveBeenCalledExactlyOnceWith('system'))
})

it('does not offer a system choice to an older backend', () => {
  render(<LanguageDialog recommended="en" allowSystem={false} onConfirm={vi.fn()} />)
  expect(screen.getAllByRole('radio')).toHaveLength(3)
  expect(screen.getByRole('radio', { name: 'English' })).toBeChecked()
  expect(screen.queryByRole('radio', { name: 'Use system language' })).not.toBeInTheDocument()
})
