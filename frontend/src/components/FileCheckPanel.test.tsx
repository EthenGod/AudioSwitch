import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { FileCheckPanel } from './FileCheckPanel'

it('shows scope before scanning, an indeterminate status during work and only confirmed results afterwards', async () => {
  const user = userEvent.setup()
  const { rerender } = render(<FileCheckPanel checking={false} />)
  expect(screen.getByRole('heading', { name: '程序文件检查' })).toBeVisible()
  expect(screen.getByRole('list', { name: '检查范围' })).toBeVisible()
  rerender(<FileCheckPanel checking />)
  expect(screen.getByRole('status')).toHaveTextContent('正在检查')
  expect(screen.queryByText(/项通过/)).not.toBeInTheDocument()
  rerender(<FileCheckPanel checking={false} result={{ Status: 'passed', Message: '全部检查通过', Directory: 'C:/声间', Entries: ['通过：主程序'] }} />)
  expect(screen.queryByRole('status')).not.toBeInTheDocument()
  expect(screen.getByRole('heading', { name: '文件完整' })).toBeVisible()
  const toggle = screen.getByRole('button', { name: '检查目录' })
  expect(toggle).toHaveAttribute('aria-expanded', 'false')
  toggle.focus(); await user.keyboard('{Enter}')
  expect(toggle).toHaveAttribute('aria-expanded', 'true')
  expect(document.getElementById(toggle.getAttribute('aria-controls')!)).not.toHaveAttribute('inert')
  await user.keyboard(' ')
  expect(toggle).toHaveAttribute('aria-expanded', 'false')
})
