import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent } from 'react'
import { ArrowDown, ArrowUp, GripVertical } from 'lucide-react'
import type { Device } from '@/data/types'
import { Button } from './ui/button'
import { DeviceIdentity } from './shared'

export function movePriority(ids: string[], from: number, to: number) {
  const next = [...ids]
  next.splice(to, 0, ...next.splice(from, 1))
  return next
}
type Drag = { id: string; pointer: number; start: number; target: number; y: number; grab: number; clientY: number; tops: number[]; heights: number[]; ids: string[] }

export function PriorityList({ order, devices, busy, readOnly, suspended = false, label, onReorder }: {
  order: string[]; devices: Device[]; busy: boolean; readOnly: boolean; suspended?: boolean; label: string
  onReorder: (ids: string[]) => Promise<boolean>
}) {
  const list = useRef<HTMLDivElement>(null)
  const active = useRef<Drag | null>(null)
  const locked = useRef(false)
  const landing = useRef<{ id: string; offset: number } | null>(null)
  const [drag, setDrag] = useState<Drag | null>(null)
  const [pending, setPending] = useState<string[] | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const disabled = busy || readOnly || pending !== null
  const shown = pending ?? order
  useLayoutEffect(() => {
    const last = landing.current
    landing.current = null
    if (!last || !pending || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const row = Array.from(list.current?.querySelectorAll<HTMLElement>('.priority-row') ?? []).find(element => element.dataset.deviceId === last.id)
    const animation = row?.animate?.([{ transform: `translateY(${last.offset}px)` }, { transform: 'translateY(0)' }], { duration: 160, easing: 'ease-out' })
    return () => animation?.cancel()
  }, [pending])
  const cancel = () => { active.current = null; setDrag(null) }
  useEffect(() => {
    if (active.current && (suspended || disabled || order.some((id, i) => id !== active.current!.ids[i]) || order.length !== active.current.ids.length)) cancel()
  }, [order, disabled, suspended])

  async function commit(ids: string[], id: string) {
    if (suspended || disabled || locked.current || ids.every((value, i) => value === order[i])) { landing.current = null; return }
    locked.current = true; setPending(ids)
    try {
      const okay = await onReorder(ids)
      setAnnouncement(okay ? `已将${devices.find(d => d.Id === id)?.Name ?? '设备'}移到第 ${ids.indexOf(id) + 1} 位` : '排序未保存，已恢复原顺序')
    } catch { setAnnouncement('排序未保存，已恢复原顺序') }
    finally { locked.current = false; setPending(null) }
  }
  function track(clientY: number) {
    const current = active.current, host = list.current
    if (!current || !host) return
    const last = current.tops.length - 1
    const firstY = current.tops[0], lastY = current.tops[last] + current.heights[last] - current.heights[current.start]
    const y = Math.max(firstY, Math.min(clientY - current.grab - host.getBoundingClientRect().top, lastY))
    const center = y + current.heights[current.start] / 2
    let target = current.start
    for (let i = 0; i < current.start; i++) if (center < current.tops[i] + current.heights[i] / 2) { target = i; break }
    for (let i = current.start + 1; i < current.tops.length; i++) if (center > current.tops[i] + current.heights[i] / 2) target = i
    // Clamping can prevent the centre from crossing an edge row, especially with unequal heights.
    if (y <= firstY) target = 0
    else if (y >= lastY) target = last
    const next = { ...current, y, target, clientY }
    active.current = next; setDrag(next)
  }
  function start(event: PointerEvent<HTMLButtonElement>, id: string) {
    if (suspended || disabled || locked.current || event.button !== 0 || !list.current) return
    const hostTop = list.current.getBoundingClientRect().top
    const rows = Array.from(list.current.querySelectorAll<HTMLElement>('.priority-row'))
    const rects = rows.map(row => row.getBoundingClientRect()), index = order.indexOf(id)
    const next = { id, pointer: event.pointerId, start: index, target: index, y: rects[index].top - hostTop, grab: event.clientY - rects[index].top, clientY: event.clientY, tops: rects.map(r => r.top - hostTop), heights: rects.map(r => r.height), ids: [...order] }
    event.currentTarget.setPointerCapture(event.pointerId)
    active.current = next; setDrag(next)
  }
  function finish(event: PointerEvent<HTMLButtonElement>) {
    const current = active.current
    if (!current || current.pointer !== event.pointerId) return
    cancel()
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
    if (!suspended && !disabled && order.length === current.ids.length && order.every((id, i) => id === current.ids[i])) {
      const next = movePriority(current.ids, current.start, current.target)
      const top = current.tops[0] + next.slice(0, current.target).reduce((sum, id) => sum + current.heights[current.ids.indexOf(id)], 0)
      landing.current = { id: current.id, offset: current.y - top }
      void commit(next, current.id)
    }
  }
  useEffect(() => {
    if (!drag) return
    const host = list.current, scroller = host?.closest<HTMLElement>('.main-content')
    let frame = 0, previous = 0
    const tick = (time: number) => {
      const current = active.current
      if (!current || !scroller) return
      const rect = scroller.getBoundingClientRect()
      const speed = current.clientY < rect.top + 48 ? -1 : current.clientY > rect.bottom - 48 ? 1 : 0
      const before = scroller.scrollTop
      if (speed) scroller.scrollTop += speed * Math.min(32, previous ? time - previous : 16) * .5
      if (scroller.scrollTop !== before) track(current.clientY)
      previous = time; frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { event.preventDefault(); cancel() } }
    const blur = () => cancel()
    window.addEventListener('keydown', escape); window.addEventListener('blur', blur)
    return () => { cancelAnimationFrame(frame); window.removeEventListener('keydown', escape); window.removeEventListener('blur', blur) }
  }, [!!drag])

  const preview = drag ? movePriority(shown, drag.start, drag.target) : shown
  return <>
    <div ref={list} className="priority-list" aria-label={label} data-dragging={!!drag || undefined}>
      {shown.map((id, index) => {
        const device = devices.find(d => d.Id === id)!
        const dragging = drag?.id === id
        const slot = preview.indexOf(id)
        const destination = drag ? drag.tops[0] + preview.slice(0, slot).reduce((sum, key) => sum + drag.heights[shown.indexOf(key)], 0) : 0
        const shift = drag ? dragging ? drag.y - drag.tops[index] : destination - drag.tops[index] : 0
        return <div className="priority-row" key={id} data-device-id={id} data-dragging={dragging || undefined} style={{ transform: `translateY(${shift}px)` }}>
          <button className="priority-grip" aria-label={`拖动 ${device.Name} 调整优先级`} disabled={disabled} data-pending={pending !== null || busy || undefined}
            onPointerDown={event => start(event, id)} onPointerMove={event => { if (active.current?.pointer === event.pointerId) track(event.clientY) }} onPointerUp={finish} onPointerCancel={cancel} onLostPointerCapture={cancel}
            onKeyDown={event => { if ((event.key === 'ArrowUp' || event.key === 'ArrowDown') && !disabled) { event.preventDefault(); const to = index + (event.key === 'ArrowUp' ? -1 : 1); if (to >= 0 && to < shown.length) void commit(movePriority(shown, index, to), id) } }}><GripVertical size={16} /></button>
          <span className="order-number">{String(slot + 1).padStart(2, '0')}</span><DeviceIdentity device={device} /><span className="priority-state">{device.Online ? '在线' : '离线'}</span>
          <div className="row-actions"><Button variant="ghost" size="icon" aria-label={`上移 ${device.Name}`} pending={busy || pending !== null} disabled={readOnly || !!drag || index === 0} onClick={() => void commit(movePriority(shown, index, index - 1), id)}><ArrowUp /></Button><Button variant="ghost" size="icon" aria-label={`下移 ${device.Name}`} pending={busy || pending !== null} disabled={readOnly || !!drag || index === shown.length - 1} onClick={() => void commit(movePriority(shown, index, index + 1), id)}><ArrowDown /></Button></div>
        </div>
      })}
      {!shown.length && <div className="empty-state"><p>暂无可排序的设备。</p></div>}
    </div>
    <span className="sr-only" role="status">{announcement}</span>
  </>
}
