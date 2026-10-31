import { Component, createRef, useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import type { Device } from '@/data/types'

/** Keep outgoing rows as fading overlays while the live rows move into place. */
export function DeviceListMotion({ devices, children }: { devices: Device[]; children: (device: Device) => ReactNode }) {
  const signature = JSON.stringify(devices)
  const [presence, setPresence] = useState({ signature, rows: devices })
  const initial = useRef(new Set(devices.map(device => device.Id)))
  useEffect(() => { initial.current.clear() }, [])
  const current = new Map(devices.map(device => [device.Id, device]))
  if (signature !== presence.signature) {
    const rows = [...devices]
    presence.rows.forEach((device, index) => {
      if (!current.has(device.Id)) rows.splice(Math.min(index, rows.length), 0, device)
    })
    setPresence({ signature, rows })
  }
  const remove = useCallback((id: string) => {
    setPresence(value => ({ ...value, rows: value.rows.filter(device => device.Id !== id) }))
  }, [])
  return <ListLayout layoutKey={JSON.stringify(devices.map(device => device.Id))}>{presence.rows.map(device => <MotionRow key={device.Id} id={device.Id}
    present={current.has(device.Id)} initial={initial.current.has(device.Id)} onRemove={remove}>
    {children(current.get(device.Id) ?? device)}
  </MotionRow>)}</ListLayout>
}

type LayoutSnapshot = { height: number; positions: Map<string, number> }
/** Measure before DOM changes so retained rows move from their actual on-screen position. */
class ListLayout extends Component<{ layoutKey: string; children: ReactNode }> {
  host = createRef<HTMLDivElement>()
  animations = new Set<Animation>()
  media: MediaQueryList | null = null
  stop = () => { this.animations.forEach(animation => animation.cancel()); this.animations.clear() }
  reduce = () => { if (this.media?.matches) this.stop() }
  componentDidMount() {
    this.media = matchMedia('(prefers-reduced-motion: reduce)')
    this.media.addEventListener('change', this.reduce)
  }
  componentWillUnmount() { this.media?.removeEventListener('change', this.reduce); this.stop() }
  getSnapshotBeforeUpdate(previous: Readonly<{ layoutKey: string; children: ReactNode }>): LayoutSnapshot | null {
    const host = this.host.current
    if (!host || previous.layoutKey === this.props.layoutKey || this.media?.matches || !host.animate) return null
    const box = host.getBoundingClientRect()
    return { height: box.height, positions: new Map(Array.from(host.children, child => [
      (child as HTMLElement).dataset.motionId!, child.getBoundingClientRect().top - box.top,
    ])) }
  }
  componentDidUpdate(_previous: unknown, _state: unknown, snapshot: LayoutSnapshot | null) {
    const host = this.host.current
    if (!snapshot || !host) return
    this.stop()
    // Exiting rows no longer occupy space, but stay at their last displayed position.
    // Re-entering rows use normal flow; any previous inline top is ignored there.
    for (const child of Array.from(host.children) as HTMLElement[]) {
      if (child.dataset.present === 'false') child.style.top = `${snapshot.positions.get(child.dataset.motionId!) ?? 0}px`
      else child.style.removeProperty('top')
    }
    const box = host.getBoundingClientRect()
    const options = { duration: box.height < snapshot.height ? 200 : 250, easing: box.height < snapshot.height ? 'ease-in' : 'ease-out' }
    const play = (element: HTMLElement, frames: Keyframe[]) => {
      const animation = element.animate(frames, options)
      this.animations.add(animation)
      animation.onfinish = () => { this.animations.delete(animation) }
    }
    for (const child of Array.from(host.children) as HTMLElement[]) {
      if (child.dataset.present === 'false') continue
      const oldTop = snapshot.positions.get(child.dataset.motionId!)
      if (oldTop === undefined) continue
      const offset = oldTop - (child.getBoundingClientRect().top - box.top)
      if (Math.abs(offset) > .5) play(child, [{ transform: `translateY(${offset}px)` }, { transform: 'translateY(0)' }])
    }
    if (Math.abs(snapshot.height - box.height) > .5) play(host, [{ height: `${snapshot.height}px` }, { height: `${box.height}px` }])
  }
  render() { return <div ref={this.host} role="rowgroup" className="device-motion-list">{this.props.children}</div> }
}

function MotionRow({ id, present, initial, onRemove, children }: {
  id: string; present: boolean; initial: boolean; onRemove: (id: string) => void; children: ReactNode
}) {
  const host = useRef<HTMLDivElement>(null)
  const [entered, setEntered] = useState(initial)
  useLayoutEffect(() => {
    // Flush the transparent first frame; CSS reverses an unfinished fade without a flash.
    if (!entered) { host.current?.getBoundingClientRect(); setEntered(true) }
  }, [entered])
  useLayoutEffect(() => {
    if (!present && host.current?.contains(document.activeElement)) {
      host.current.closest<HTMLElement>('[role="table"]')?.focus({ preventScroll: true })
    }
  }, [present])
  useEffect(() => {
    if (present) return
    const media = matchMedia('(prefers-reduced-motion: reduce)')
    const finish = () => onRemove(id)
    const timer = window.setTimeout(finish, media.matches ? 0 : 200)
    const reduce = () => { if (media.matches) finish() }
    media.addEventListener('change', reduce)
    return () => { clearTimeout(timer); media.removeEventListener('change', reduce) }
  }, [id, present, onRemove])
  return <div ref={host} className="device-motion-row" data-visible={present && entered} data-present={present} data-motion-id={id}
    aria-hidden={!present || undefined} inert={!present || undefined}>
    {children}
  </div>
}
