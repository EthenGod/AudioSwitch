async (page) => {
  const checks=[]
  await page.getByText('只读等待夹具：辅助进程只等待取消，不读取或写入音频。所有保存与切换均被拒绝。').waitFor()
  await page.getByRole('button',{name:'设置当前输出'}).click();await page.getByRole('button',{name:'编辑 Dolby 方案'}).click()
  const editor=page.getByRole('dialog',{name:'Dolby 方案',exact:true})
  await editor.getByRole('button',{name:'读取当前 Dolby 并填入'}).click();await editor.getByRole('button',{name:'取消读取'}).waitFor()
  await editor.getByRole('button',{name:'取消读取'}).click();await editor.getByText('读取已取消，原草稿已保留。',{exact:true}).waitFor()
  checks.push('capture-only child cancellation confirmed in UI')
  await editor.getByRole('button',{name:'关闭 Dolby 编辑器'}).click();await editor.waitFor({state:'detached'});await page.getByRole('button',{name:'关闭设备设置'}).click()
  await page.getByRole('button',{name:'应用设置',exact:true}).click();await page.getByRole('button',{name:/文件检查/}).click()
  const files=page.getByRole('dialog',{name:'文件检查',exact:true})
  await files.getByRole('button',{name:'开始检查'}).click();await files.getByRole('button',{name:'取消检查'}).click()
  await files.getByRole('button',{name:'重新检查'}).waitFor();checks.push('maintenance cancellation confirms child exit')
  await page.screenshot({path:'output/desktop/stage44c-cancel.png'})
  // Leave a maintenance task in flight for the external exact-PID window-close check.
  await files.getByRole('button',{name:'重新检查'}).click();await files.getByRole('button',{name:'取消检查'}).waitFor()
  return {count:checks.length,checks,maintenanceRunning:true}
}
