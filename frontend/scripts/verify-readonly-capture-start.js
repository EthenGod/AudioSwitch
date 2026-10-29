async (page) => {
  await page.getByText('只读等待夹具：辅助进程只等待取消，不读取或写入音频。所有保存与切换均被拒绝。').waitFor()
  await page.getByRole('button',{name:'设置当前输出'}).click();await page.getByRole('button',{name:'编辑 Dolby 方案'}).click()
  const editor=page.getByRole('dialog',{name:'Dolby 方案',exact:true})
  await editor.getByRole('button',{name:'读取当前 Dolby 并填入'}).click()
  await editor.getByRole('button',{name:'取消读取'}).waitFor()
  return {captureRunning:true}
}
