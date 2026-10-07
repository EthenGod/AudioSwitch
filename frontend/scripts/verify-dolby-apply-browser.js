async (page) => {
  const checks=[], errors=[], requests=[]
  const check=(value,label)=>{if(!value)throw new Error(label);checks.push(label)}
  page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>requests.push(r.url()))
  await page.setViewportSize({width:1100,height:740});await page.goto('http://127.0.0.1:4173/');await page.getByRole('table').waitFor()
  for(const light of [false,true]) {
    if(light) { await page.getByRole('button',{name:'切换到浅色模式'}).click();await page.getByRole('button',{name:'切换到深色模式'}).waitFor() }
    await page.getByRole('button',{name:'设置当前输出'}).click();await page.getByRole('button',{name:'编辑 Dolby 方案'}).click()
    const dialog=page.getByRole('dialog',{name:'Dolby 方案',exact:true}), apply=dialog.getByRole('button',{name:'保存并应用 Dolby（模拟）'})
    await dialog.getByRole('button',{name:'模拟读取 Dolby 并填入'}).click();await dialog.getByText('已填入示例方案，未读取实际 Dolby。').waitFor()
    await apply.click();await dialog.getByText('示例方案已保存，正在模拟应用…').waitFor()
    check(await apply.isDisabled(),'repeated application blocked')
    check(await dialog.getByRole('button',{name:'仅保存 Dolby（模拟）'}).isDisabled(),'only-save blocked while applying')
    await dialog.getByText('模拟应用完成，未修改系统设置。').waitFor()
    check(await apply.isEnabled(),'completed result releases controls')
    for(const base of [{width:1100,height:740},{width:980,height:680}])for(const scale of [1,1.25,1.5,2]) {
      await page.setViewportSize({width:Math.round(base.width/scale),height:Math.round(base.height/scale)})
      check(await dialog.evaluate(el=>{const footer=el.querySelector('.sheet-footer'),r=footer.getBoundingClientRect();return el.scrollWidth<=el.clientWidth+1&&r.top>=0&&r.bottom<=innerHeight+1&&Array.from(footer.querySelectorAll('button')).every(b=>{const x=b.getBoundingClientRect();return x.left>=0&&x.right<=innerWidth&&x.bottom<=innerHeight})}),`${light}/${base.width}/${scale}: all action buttons visible`)
    }
    await page.setViewportSize({width:1100,height:740});await page.screenshot({path:`output/playwright/dolby-apply-${light?'light':'dark'}.png`})
    await apply.click();await dialog.getByRole('button',{name:'取消应用'}).click()
    await dialog.getByText('正在模拟停止与恢复检查…').waitFor();check(await apply.isDisabled(),'cancellation acknowledgement keeps controls locked')
    await dialog.getByText('模拟应用已停止，未修改系统设置。').waitFor()
    check(await apply.isEnabled(),'stopped worker releases controls')
    await apply.click();await dialog.getByRole('button',{name:'关闭 Dolby 编辑器'}).click();await dialog.waitFor({state:'detached'})
    check(await page.getByRole('button',{name:'编辑 Dolby 方案'}).evaluate(el=>el===document.activeElement),'close waits then restores nested focus')
    await page.getByRole('button',{name:'关闭设备设置'}).click()
  }
  check(errors.length===0,'no runtime errors');check(requests.every(url=>url.startsWith('http://127.0.0.1:4173/')),'mock only loads local assets')
  return {count:checks.length,checks,errors}
}
