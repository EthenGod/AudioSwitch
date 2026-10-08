// Use only with AudioSwitch.Tests.exe --panel-readonly-host, never a normal backend.
async (page) => {
  const checks=[], errors=[]
  page.on('pageerror',e=>errors.push(e.message))
  const check=(ok,name)=>{if(!ok)throw new Error(name);checks.push(name)}
  const banner=page.getByText('只读联调入口：设备和读取结果来自本机；所有保存、切换和应用请求均被拒绝。未运行托盘自动规则。')
  await banner.waitFor(); await page.getByRole('table').waitFor()
  check(await page.getByRole('row').count()>1,'real endpoint rows rendered through production Tauri bridge')
  await page.getByRole('button',{name:'设置当前输出'}).click()
  await page.getByRole('button',{name:'编辑 Dolby 方案'}).click()
  const editor=page.getByRole('dialog',{name:'Dolby 方案',exact:true})
  await editor.getByRole('button',{name:'读取当前 Dolby 并填入'}).click()
  await editor.getByText('已填入当前方案；仅保存后才会更新设备预设。',{exact:true}).waitFor({timeout:20000})
  check(await editor.getByRole('button',{name:'仅保存 Dolby',exact:true}).isEnabled(),'native capture finishes and keeps save explicit')
  check(await editor.getByRole('switch').isChecked(),'captured plan fills enabled draft')
  await page.screenshot({path:'output/desktop/stage44c-real-dolby.png'})
  // This fixture is guarded above and contains no save implementation.
  await editor.getByRole('button',{name:'仅保存 Dolby',exact:true}).click()
  await editor.getByText('只读联调禁止保存、设备切换、音效应用及其他写入。',{exact:true}).waitFor()
  check(await editor.isVisible(),'read-only host rejects mutation without closing or losing draft')
  await editor.getByRole('button',{name:'关闭 Dolby 编辑器'}).click(); await editor.waitFor({state:'detached'})
  await page.getByRole('button',{name:'关闭设备设置'}).click()
  await page.getByRole('button',{name:'应用设置',exact:true}).click()
  await page.getByRole('button',{name:/文件检查/}).click()
  const files=page.getByRole('dialog',{name:'文件检查',exact:true})
  await files.getByRole('button',{name:'开始检查'}).click()
  await files.getByRole('button',{name:'重新检查'}).waitFor({timeout:20000})
  const result=await files.innerText()
  check(result.includes('检查通过') || result.includes('文件完整'),'real file integrity worker reports completion')
  check(result.includes('AudioSwitch.exe'),'real file list reaches UI')
  await page.screenshot({path:'output/desktop/stage44c-real-files.png'})
  await files.getByRole('button',{name:'关闭检查窗口'}).click();await files.waitFor({state:'detached'})
  check(errors.length===0,'no WebView runtime errors')
  return {count:checks.length,checks,errors,fileResult:result}
}
