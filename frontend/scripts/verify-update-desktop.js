async (page) => {
  await page.getByText('只读联调入口：设备和读取结果来自本机；所有保存、切换和应用请求均被拒绝。未运行托盘自动规则。').waitFor()
  await page.getByRole('button',{name:'应用设置',exact:true}).click()
  await page.getByRole('button',{name:/检查更新/}).click()
  const dialog=page.getByRole('dialog',{name:'检查更新',exact:true})
  await dialog.getByRole('button',{name:'开始检查'}).click()
  await dialog.getByRole('button',{name:'重新检查'}).waitFor({timeout:40000})
  const text=await dialog.innerText()
  if(!text.includes('最新正式版本：') && !text.includes('暂未找到可用')) throw new Error(text)
  await page.screenshot({path:'output/desktop/stage44c-real-update.png'})
  await dialog.getByRole('button',{name:'关闭检查窗口'}).click()
  return {completed:true,result:text}
}
