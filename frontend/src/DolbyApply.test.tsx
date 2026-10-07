import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DolbySheet } from './components/DolbySheet'
import { createMockGateway } from './data/mock'
import { createDesktopGateway, OperationFailure } from './data/desktop'
import { emptyDolby, type DolbyOperation } from './data/dolby'
import type { Snapshot, UiGateway } from './data/types'
import { backendFixture } from './test/backendFixture'
const profile = { ...emptyDolby(), Enabled:false }
const button = () => screen.getByRole('button', { name:'保存并应用 Dolby（模拟）' })
const operation = (token: string, Status: DolbyOperation['Status'] = 'running', Message = '已保存，尚未应用完成'): DolbyOperation => ({ Token:token, DeviceId:'demo-headphones', Status, Message })
async function setup(factory: (snapshot: Snapshot) => Partial<UiGateway> = () => ({}), selected = 0) {
  const base = createMockGateway(0); await base.saveDolby('demo-headphones', profile, null)
  const snapshot = await base.read(), gateway = { ...base, ...factory(snapshot) } as UiGateway
  const onSaved = vi.fn(), onClose = vi.fn(), onFailure = vi.fn(), onResult = vi.fn()
  const view = render(<DolbySheet device={snapshot.Devices[selected]} snapshot={snapshot} gateway={gateway} readOnly={false} onSaved={onSaved} onClose={onClose} onFailure={onFailure} onResult={onResult} />)
  return { gateway, snapshot, onSaved, onClose, onFailure, onResult, view }
}
describe('Dolby save and apply', () => {
  it('disables repeated apply and only-save until the owned result is complete', async () => {
    const start = vi.fn(), read = vi.fn(async (token: string) => operation(token,'applied','本次应用成功'))
    await setup(snapshot => { start.mockImplementation(async (_id,_p,_e,token) => ({ snapshot, operation:operation(token) })); return { startDolbyApply:start, readDolbyApply:read } })
    fireEvent.click(button()); fireEvent.click(button())
    await screen.findByText('已保存，尚未应用完成')
    expect(start).toHaveBeenCalledOnce(); expect(screen.getByRole('button',{name:'仅保存 Dolby（模拟）'})).toBeDisabled()
    await screen.findByText('本次应用成功'); expect(button()).toBeEnabled()
    expect(start.mock.calls[0].slice(0,3)).toEqual(['demo-headphones',profile,profile])
  })
  it('keeps saved state distinct from driver failure and retains the edited draft', async () => {
    const { onResult } = await setup(snapshot => ({ startDolbyApply:async (_id,_p,_e,t) => ({snapshot,operation:operation(t)}), readDolbyApply:async t=>operation(t,'error','方案已保存，但恢复失败，请检查音效。') }))
    fireEvent.change(screen.getByRole('combobox',{name:'人声增强'}),{target:{value:'on'}})
    fireEvent.click(button()); await screen.findByRole('alert')
    expect(screen.getByRole('alert')).toHaveTextContent('恢复失败'); expect(screen.getByRole('combobox',{name:'人声增强'})).toHaveValue('on')
    expect(onResult).toHaveBeenCalledWith('方案已保存，但恢复失败，请检查音效。',true)
  })
  it('cancel acknowledgement does not close until restoration has settled', async () => {
    let done = false
    const { onClose } = await setup(snapshot => ({ startDolbyApply:async (_i,_p,_e,t)=>({snapshot,operation:operation(t)}), cancelDolbyApply:async t=>operation(t,'cancelling','正在恢复检查'), readDolbyApply:async t=>operation(t,done?'cancelled':'cancelling',done?'取消后恢复未通过核验':'正在恢复检查') }))
    fireEvent.click(button()); await screen.findByText('已保存，尚未应用完成'); fireEvent.click(screen.getByRole('button',{name:'关闭'}))
    await screen.findByText('正在恢复检查'); expect(onClose).not.toHaveBeenCalled(); expect(button()).toBeDisabled()
    done = true; await waitFor(()=>expect(onClose).toHaveBeenCalledOnce())
  })
  it('closing during submission cancels the returned task without duplicate saving', async () => {
    let finish!: (r:{snapshot:Snapshot;operation:DolbyOperation})=>void, token = ''
    const cancel = vi.fn(async (t:string)=>operation(t,'cancelled','应用已停止'))
    const { snapshot, onClose } = await setup(()=>({ startDolbyApply:async (_i,_p,_e,t)=>{token=t;return new Promise(r=>{finish=r})}, cancelDolbyApply:cancel }))
    fireEvent.click(button()); fireEvent.click(screen.getByRole('button',{name:'关闭 Dolby 编辑器'})); expect(onClose).not.toHaveBeenCalled()
    finish({snapshot,operation:operation(token)})
    await waitFor(()=>expect(cancel).toHaveBeenCalledWith(token)); await waitFor(()=>expect(onClose).toHaveBeenCalledOnce())
  })
  it('lost start response keeps its token for status recovery and blocks resubmission', async () => {
    let token = ''
    const read = vi.fn(async(t:string)=>operation(t,'error','后台确认应用失败，方案保留'))
    const start = vi.fn(async (_i:string,_p:unknown,_e:unknown,t:string)=>{token=t;throw new OperationFailure('响应丢失，请检查本次应用',undefined,true)})
    await setup(()=>({startDolbyApply:start,readDolbyApply:read}))
    fireEvent.click(button()); await screen.findByText('响应丢失，请检查本次应用'); expect(button()).toBeDisabled()
    fireEvent.click(screen.getByRole('button',{name:'检查本次应用'})); await screen.findByText('后台确认应用失败，方案保留')
    expect(read).toHaveBeenCalledWith(token); expect(start).toHaveBeenCalledOnce(); expect(button()).toBeDisabled()
  })
  it('unmount sends cancellation for only its own active job', async () => {
    const cancel = vi.fn(async(t:string)=>operation(t,'cancelling'))
    const {view} = await setup(snapshot=>({startDolbyApply:async(_i,_p,_e,t)=>({snapshot,operation:operation(t)}),cancelDolbyApply:cancel}))
    fireEvent.click(button()); await screen.findByText('已保存，尚未应用完成'); view.unmount(); await waitFor(()=>expect(cancel).toHaveBeenCalledOnce())
  })
  it('noncurrent output cannot apply', async () => { await setup(()=>({}),1); expect(button()).toBeDisabled() })
  it('desktop distinguishes API support, saved data and asynchronous results', async () => {
    const raw = {...backendFixture(),PanelApiVersion:5,PreferencesSaved:true,OperationError:null}, token='0123456789abcdef0123456789abcdef'
    const call = vi.fn(async()=>({...raw, Preferences:{...raw.Preferences,DeviceProfiles:{...raw.Preferences.DeviceProfiles,'endpoint-a':{Volume:null,SpatialFormat:null,Dolby:profile}}},DolbyOperation:{...operation(token),DeviceId:'endpoint-a'}}))
    const g = createDesktopGateway(async()=>raw,call); await g.read()
    expect((await g.startDolbyApply('endpoint-a',profile,null,token)).operation.Status).toBe('running')
    expect(call).toHaveBeenCalledWith('start_dolby_apply',{id:'endpoint-a',profile,expected:null,token})
    const old = createDesktopGateway(async()=>({...raw,PanelApiVersion:4}),call); await old.read(); await expect(old.startDolbyApply('endpoint-a',profile,null,token)).rejects.toThrow('不支持')
  })
  it('foreign application results are rejected instead of reporting success', async () => {
    const raw = {...backendFixture(),PanelApiVersion:5}, call=vi.fn(async()=>operation('foreign','applied'))
    const g=createDesktopGateway(async()=>raw,call); await g.read(); await expect(g.readDolbyApply('mine')).rejects.toThrow('未确认')
  })
})
