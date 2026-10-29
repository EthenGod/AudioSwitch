async (page) => {
  const checks=[], errors=[], requests=[]
  const check=(value,label)=>{if(!value)throw new Error(label);checks.push(label)}
  page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>requests.push(r.url()))
  await page.setViewportSize({width:1100,height:740});await page.goto('http://127.0.0.1:4173/');await page.getByRole('table').waitFor()
  for(const light of [false,true]) {
    if(light) { await page.getByRole('button',{name:'切换到浅色模式'}).click();await page.getByRole('button',{name:'切换到深色模式'}).waitFor() }
    await page.getByRole('button',{name:'设置当前输出'}).click();await page.getByRole('button',{name:'编辑 Dolby 方案'}).click()
    const dialog=page.getByRole('dialog',{name:'Dolby 方案',exact:true})
    await dialog.getByRole('button',{name:'模拟读取 Dolby 并填入'}).click();await dialog.getByText('已填入示例方案，未读取实际 Dolby。').waitFor()
    await dialog.getByRole('button',{name:'20 段原始值'}).click()
    const rawBefore=await dialog.locator('.dolby-eq input').evaluateAll(es=>es.map(e=>e.value))
    await dialog.getByRole('button',{name:'10 点 dB'}).click()
    const slider=dialog.getByRole('slider',{name:'32 Hz dB 滑块'});await slider.scrollIntoViewIfNeeded()
    const box=await slider.boundingBox();await page.mouse.click(box.x+box.width*((-27/16+12)/24),box.y+box.height/2)
    await dialog.getByRole('button',{name:'20 段原始值'}).click()
    check(JSON.stringify(await dialog.locator('.dolby-eq input').evaluateAll(es=>es.map(e=>e.value)))===JSON.stringify(rawBefore),'view changes and stationary click preserve exact raw curve')
    await dialog.getByRole('button',{name:'10 点 dB'}).click()
    await dialog.getByRole('spinbutton',{name:'32 Hz dB',exact:true}).fill('-6.25')
    await dialog.getByRole('spinbutton',{name:'32 Hz dB',exact:true}).blur()
    check(await dialog.getByRole('spinbutton',{name:'32 Hz dB',exact:true}).inputValue()==='-6.25','negative numeric entry works')
    for(const advanced of [false,true]) {
      await dialog.getByRole('button',{name:advanced?'20 段原始值':'10 点 dB'}).click()
      for(const base of [{width:1100,height:740},{width:980,height:680}])for(const scale of [1,1.25,1.5,2]) {
        await page.setViewportSize({width:Math.round(base.width/scale),height:Math.round(base.height/scale)})
        check(await dialog.evaluate(el=>{const r=el.querySelector('.sheet-footer').getBoundingClientRect();return el.scrollWidth<=el.clientWidth+1&&r.top>=0&&r.bottom<=innerHeight+1}),`${light}/${advanced}/${base.width}/${scale}: layout and footer`)
      }
      await page.setViewportSize({width:1100,height:740})
      await dialog.locator('.sheet-scroll').evaluate((el,raw)=>{el.scrollTop=raw?450:0},advanced)
      await page.screenshot({path:`output/playwright/dolby-${light?'light':'dark'}-${advanced?'raw':'editor'}.png`})
    }
    await dialog.getByRole('button',{name:'仅保存 Dolby（模拟）'}).click();await dialog.waitFor({state:'detached'})
    await page.getByRole('button',{name:'编辑 Dolby 方案'}).click();await dialog.getByRole('button',{name:'20 段原始值'}).click()
    check(await dialog.getByRole('spinbutton',{name:'第 1 段原始值',exact:true}).inputValue()==='-100','only-save persists exact edited curve in mock state')
    await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'})
    check(await page.getByRole('button',{name:'编辑 Dolby 方案'}).evaluate(el=>el===document.activeElement),'nested close restores focus and retains basic sheet')
    await page.getByRole('button',{name:'关闭设备设置'}).click()
  }
  check(errors.length===0,'no runtime errors');check(requests.every(url=>url.startsWith('http://127.0.0.1:4173/')),'mock only loads local assets')
  return {count:checks.length,checks,errors}
}
