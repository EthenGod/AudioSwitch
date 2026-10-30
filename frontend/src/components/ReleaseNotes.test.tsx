import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { ReleaseNotes, renderReleaseNotes } from './ReleaseNotes'
import { demoNotes } from '@/data/demoNotes'

it('renders the preserved GitHub website fallback with nested code and later list content', () => {
  render(<ReleaseNotes source={'<h2>网页更新</h2><div><pre><code>例子</code></pre></div><ul><li>保留后续内容</li></ul>'} />)
  expect(screen.getByRole('heading', { name: '网页更新' })).toBeVisible()
  expect(screen.getByRole('listitem')).toHaveTextContent('保留后续内容')
})

it('renders GitHub tables, tasks, formatting, details and emoji shortcodes without changing code', () => {
  const { container } = render(<ReleaseNotes source={demoNotes + '\n\n```text\n:sparkles: <script>\n```'} />)
  expect(screen.getByRole('heading', { name: '✨ 更新内容' })).toBeInTheDocument()
  expect(screen.getByRole('table')).toBeInTheDocument()
  expect(screen.getByRole('checkbox', { name: '已完成' })).toBeDisabled()
  expect(container.querySelector('del')).toHaveTextContent('旧版说明')
  expect(container.querySelector('details summary')).toHaveTextContent('更多说明')
  expect(container.querySelector('pre code')).toHaveTextContent(':sparkles: <script>')
  expect(screen.getByRole('link', { name: '查看项目' })).toHaveAttribute('rel', 'noopener noreferrer')
})
it('removes scripts, events, injected styles, active forms and non-web URLs', () => {
  const html = renderReleaseNotes('<script>alert(1)</script><iframe src="https://example.com"></iframe><style>body{display:none}</style><a href="javascript:alert(1)" onclick="alert(1)">bad</a><img src="file:///C:/secret" onerror="alert(1)"><input type="text"><form action="https://example.com">x</form>')
  const { container } = render(<div dangerouslySetInnerHTML={{ __html: html }} />)
  expect(container.querySelector('script,iframe,style,form,input,img,[onclick],[onerror],[style]')).toBeNull()
  expect(container.querySelector('a')).not.toHaveAttribute('href')
})
it('restricts image sources to https and disables referrer forwarding', () => {
  const { container } = render(<ReleaseNotes source={'![download](https://img.shields.io/badge/download-blue)\n![bad](data:image/svg+xml,x)'} />)
  expect(container.querySelectorAll('img')).toHaveLength(1)
  expect(container.querySelector('img')).toHaveAttribute('referrerpolicy', 'no-referrer')
})
