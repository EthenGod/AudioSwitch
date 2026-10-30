import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { FileCheckResult } from './FileCheckResult'

it('puts problems first and counts check entries rather than inventing a file total', () => {
  render(<FileCheckResult status="repairable" message="发现文件缺损" entries={['通过：完整包内全部 13 个内容文件', '缺失：面板.exe', '未知检查说明']} directory="C:/例子" />)
  expect(screen.getByRole('heading')).toHaveTextContent('需要修复')
  expect(screen.getAllByRole('listitem')[0]).toHaveTextContent('缺失：面板.exe')
  expect(screen.getByText('1 项通过')).toBeInTheDocument()
  expect(screen.getByText('1 项异常')).toBeInTheDocument()
})
it('keeps old backend details and never reports a cancelled or incomplete check as passed', () => {
  render(<FileCheckResult status="cancelled" message="已取消" legacy="旧后台原始结果" />)
  expect(screen.getByRole('heading')).toHaveTextContent('检查未完成')
  expect(screen.getByText('旧后台原始结果')).toBeInTheDocument()
  expect(screen.queryByLabelText('检查项目统计')).toBeNull()
})
