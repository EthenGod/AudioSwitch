import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DolbyFields, DolbySheet } from './components/DolbySheet'
import { dolbyProfile, emptyDolby, toTen, toTwenty, type DolbyRead } from './data/dolby'
import { createMockGateway } from './data/mock'
import { createDesktopGateway, mapSnapshot } from './data/desktop'
import { backendFixture } from './test/backendFixture'
const curve = [-27,16,26,56,77,60,43,26,25,24,23,40,56,77,51,21,-22,-31,-42,-66]
const profile = () => ({ ...emptyDolby(), MainProfile:4 as const, SubProfile:4 as const, Eq:[...curve] })
async function setup(overrides = {}) {
  const gateway = { ...createMockGateway(0), ...overrides }, snapshot = await gateway.read()
  snapshot.DolbyProfiles = { 'demo-headphones':profile() }
  const onSaved = vi.fn(), onClose = vi.fn(), onFailure = vi.fn()
  render(<DolbySheet device={snapshot.Devices[0]} snapshot={snapshot} gateway={gateway} readOnly={false} onSaved={onSaved} onClose={onClose} onFailure={onFailure} />)
  return { gateway, snapshot, onSaved, onClose, onFailure }
}
describe('Dolby editor and calibrated EQ', () => {
  it('matches independently captured Access curves including extrapolation', () => {
    expect(toTwenty(toTen(curve))).toEqual(curve)
    const points = toTen(curve); points[0] = 173/16; points[1] = 189/16; points[9] = 12
    expect(toTwenty(points)).toEqual([173,189,26,56,77,60,43,26,25,24,23,40,56,77,51,21,-22,77,192,192])
    expect(toTwenty(Array(10).fill(-0.03125))).toEqual(Array(20).fill(-1))
    expect(() => toTwenty(Array(10).fill(NaN))).toThrow()
    expect(() => dolbyProfile({ ...profile(), Eq:Array(20).fill(193) })).toThrow()
    expect(dolbyProfile({ ...emptyDolby(), Enabled:false, SurroundStrength:0 })?.Enabled).toBe(false)
  })
  it('view switches preserve every raw detail, editing raw touches one band', () => {
    const p = profile(); p.Eq[5] = -191; const changed = vi.fn()
    render(<DolbyFields value={p} onChange={changed} />)
    fireEvent.click(screen.getByRole('button',{ name:'20 段原始值' })); fireEvent.click(screen.getByRole('button',{ name:'10 点 dB' }))
    expect(changed).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button',{ name:'20 段原始值' }))
    fireEvent.change(screen.getByRole('spinbutton',{ name:'第 7 段原始值' }),{ target:{ value:'0' } })
    expect(changed.mock.calls[0][0].Eq).toEqual(p.Eq.map((v,i)=>i === 6 ? 0 : v))
  })
  it('editing ten points uses calibrated interpolation', () => {
    const changed = vi.fn(); render(<DolbyFields value={profile()} onChange={changed} />)
    fireEvent.change(screen.getByRole('spinbutton',{ name:'500 Hz dB' }),{ target:{ value:'6.3' } })
    const points=toTen(curve); points[4]=6.3
    expect(changed.mock.calls[0][0].Eq).toEqual(toTwenty(points))
  })
  it('only-save sends the original baseline and never starts capture automatically', async () => {
    const saveDolby = vi.fn(async()=>mapSnapshot(backendFixture())), startDolbyRead = vi.fn()
    const { onSaved } = await setup({ saveDolby, startDolbyRead })
    fireEvent.click(screen.getByRole('button',{ name:'20 段原始值' })); fireEvent.click(screen.getByRole('button',{ name:'仅保存 Dolby（模拟）' }))
    await waitFor(()=>expect(onSaved).toHaveBeenCalledOnce())
    expect(saveDolby).toHaveBeenCalledWith('demo-headphones',profile(),profile()); expect(startDolbyRead).not.toHaveBeenCalled()
  })
  it('cancel after editing does not save', async () => {
    const saveDolby=vi.fn(), { onClose }=await setup({ saveDolby })
    fireEvent.change(screen.getByRole('combobox',{ name:'Dolby 总开关' }),{ target:{ value:'off' } })
    fireEvent.click(screen.getByRole('button',{ name:'取消' }))
    await waitFor(()=>expect(onClose).toHaveBeenCalled()); expect(saveDolby).not.toHaveBeenCalled()
  })
  it('read failures preserve the existing draft and keep save available', async () => {
    await setup({ startDolbyRead:vi.fn(async()=>({ Token:'r',Status:'error',Message:'设备不支持 Dolby' } as DolbyRead)) })
    fireEvent.click(screen.getByRole('button',{ name:'模拟读取 Dolby 并填入' }))
    await screen.findByText('设备不支持 Dolby')
    expect(screen.getByRole('spinbutton',{ name:'32 Hz dB' })).toHaveValue(curve[0]/16)
    expect(screen.getByRole('button',{ name:'仅保存 Dolby（模拟）' })).toBeEnabled()
  })
  it('close during pending start cancels the returned job before closing', async () => {
    let finish!: (v:DolbyRead)=>void
    const cancel=vi.fn(async()=>({Token:'r',Status:'cancelled',Message:'已取消'} as DolbyRead))
    const { onClose }=await setup({ startDolbyRead:()=>new Promise<DolbyRead>(resolve=>{finish=resolve}), cancelDolbyRead:cancel })
    fireEvent.click(screen.getByRole('button',{name:'模拟读取 Dolby 并填入'})); fireEvent.click(screen.getByRole('button',{name:'关闭 Dolby 编辑器'}))
    expect(onClose).not.toHaveBeenCalled(); finish({Token:'r',Status:'running',Message:'读取中'})
    await waitFor(()=>expect(cancel).toHaveBeenCalledWith('r')); await waitFor(()=>expect(onClose).toHaveBeenCalled())
  })
  it('desktop only-save confirms actual saved fields and rejects duplicate submissions', async () => {
    const raw = { ...backendFixture(), PanelApiVersion:4, PreferencesSaved:true, OperationError:null }
    const call=vi.fn(async()=>({ ...raw, Preferences:{...raw.Preferences, DeviceProfiles:{...raw.Preferences.DeviceProfiles,'endpoint-a':{Volume:null,SpatialFormat:null,Dolby:profile()}}} }))
    const gateway=createDesktopGateway(async()=>raw,call); await gateway.read()
    const saved=gateway.saveDolby('endpoint-a',profile(),null)
    await expect(gateway.saveDolby('endpoint-a',profile(),null)).rejects.toThrow('上一个')
    expect((await saved).DolbyProfiles?.['endpoint-a']).toEqual(profile())
    expect(call).toHaveBeenCalledWith('save_dolby',{ id:'endpoint-a',profile:profile(),expected:null })
  })
  it('unconfirmed save cannot be retried until refresh and old backends cannot save', async () => {
    const raw={...backendFixture(),PanelApiVersion:4}, call=vi.fn(async()=>{throw {message:'超时',requiresRefresh:true}})
    const g=createDesktopGateway(async()=>raw,call); await g.read()
    await expect(g.saveDolby('endpoint-a',null,null)).rejects.toThrow('超时')
    await expect(g.saveDolby('endpoint-a',null,null)).rejects.toThrow('刷新'); expect(call).toHaveBeenCalledOnce()
    const old=createDesktopGateway(async()=>backendFixture(),call); await old.read(); await expect(old.saveDolby('endpoint-a',null,null)).rejects.toThrow('不支持')
  })
})
