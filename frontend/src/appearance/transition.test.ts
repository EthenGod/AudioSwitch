import { afterEach, expect, it, vi } from 'vitest'
import { createAppearanceTransition } from './transition'

const root = document.documentElement
const classic = { dark: true, style: 'classic' }
const alternate = { dark: false, style: 'fixture-style' }
const controllers: ReturnType<typeof createAppearanceTransition>[] = []
const create = () => { const value = createAppearanceTransition(); controllers.push(value); return value }
afterEach(() => {
  controllers.splice(0).forEach(controller => controller.dispose())
  vi.restoreAllMocks()
  Reflect.deleteProperty(document, 'startViewTransition')
  Reflect.deleteProperty(root, 'animate')
})

function captures() {
  const pending: { commit: () => void; finish: () => void; skipTransition: ReturnType<typeof vi.fn> }[] = []
  const start = vi.fn((commit: () => void) => {
    let finish!: () => void
    const finished = new Promise<void>(resolve => { finish = resolve })
    const skipTransition = vi.fn()
    pending.push({ commit, finish, skipTransition })
    return { ready: Promise.resolve(), finished, skipTransition }
  })
  Object.defineProperty(document, 'startViewTransition', { configurable: true, value: start })
  return { start, pending }
}

it('applies the first confirmed appearance immediately and does no work for identical snapshots', () => {
  const { start } = captures(), controller = create()
  controller.update(classic, false)
  controller.update(alternate, true)
  expect(root).toHaveAttribute('data-ui-style', 'fixture-style')
  expect(root).not.toHaveClass('dark')
  const write = vi.spyOn(root.classList, 'toggle')
  for (let i = 0; i < 100; i++) controller.update({ ...alternate })
  expect(start).not.toHaveBeenCalled()
  expect(write).not.toHaveBeenCalled()
})

it('crossfades the root including portal content without replacing controls, focus or scroll', async () => {
  const { pending } = captures(), controller = create()
  const portal = document.createElement('section'), input = document.createElement('input')
  portal.append(input); document.body.append(portal)
  input.value = '未保存的草稿'; input.focus(); portal.scrollTop = 51
  try {
    controller.update(classic); controller.update(alternate)
    expect(root).toHaveClass('dark')
    expect(root).toHaveAttribute('data-appearance-transition')
    pending[0].commit()
    expect(root).toHaveAttribute('data-ui-style', 'fixture-style')
    expect(root).not.toHaveClass('dark')
    expect(portal.firstChild).toBe(input)
    expect(input).toHaveFocus(); expect(input.value).toBe('未保存的草稿')
    expect(portal.scrollTop).toBe(51)
    pending[0].finish(); await Promise.resolve()
    expect(root).not.toHaveAttribute('data-appearance-transition')
  } finally { portal.remove() }
})

it('ignores late captures after rapid changes or unmount and releases the old transition', async () => {
  const { pending } = captures(), controller = create()
  controller.update(classic); controller.update(alternate); controller.update(classic)
  expect(pending[0].skipTransition).toHaveBeenCalledTimes(1)
  pending[1].commit(); pending[0].commit(); pending[0].finish(); await Promise.resolve()
  expect(root).toHaveAttribute('data-ui-style', 'classic')
  expect(root).toHaveClass('dark')
  expect(root).toHaveAttribute('data-appearance-transition')
  controller.dispose()
  root.dataset.uiStyle = 'another-window'
  pending[1].commit(); pending[1].finish(); await Promise.resolve()
  expect(root).toHaveAttribute('data-ui-style', 'another-window')
  expect(root).not.toHaveAttribute('data-appearance-transition')
  // React StrictMode can clean up and immediately set up the same hook again.
  controller.update(alternate)
  expect(root).toHaveAttribute('data-ui-style', 'fixture-style')
  expect(pending).toHaveLength(2)
})

it('skips animation for reduced motion and hidden documents', () => {
  const { start } = captures(), controller = create()
  controller.update(classic)
  const media = window.matchMedia
  vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ ...media(query), matches: true }))
  controller.update(alternate)
  expect(root).toHaveAttribute('data-ui-style', 'fixture-style')
  vi.restoreAllMocks()
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
  controller.update(classic)
  expect(root).toHaveClass('dark')
  expect(start).not.toHaveBeenCalled()
})

it('falls back on unsupported browsers and cancels its temporary fade on unmount', async () => {
  let finish!: () => void
  const finished = new Promise<void>(resolve => { finish = resolve }), cancel = vi.fn(finish)
  const animate = vi.fn(() => ({ finished, cancel }))
  Object.defineProperty(root, 'animate', { configurable: true, value: animate })
  const controller = create()
  controller.update(classic); controller.update(alternate)
  expect(root).toHaveAttribute('data-ui-style', 'fixture-style')
  expect(animate).toHaveBeenCalledTimes(1)
  controller.dispose(); await Promise.resolve()
  expect(cancel).toHaveBeenCalledTimes(1)
  expect(root).not.toHaveAttribute('data-appearance-transition')
})

it('applies the preference even if a browser rejects snapshot creation', () => {
  Object.defineProperty(document, 'startViewTransition', { configurable: true, value: () => { throw new Error('capture failed') } })
  const controller = create()
  controller.update(classic); controller.update(alternate)
  expect(root).toHaveAttribute('data-ui-style', 'fixture-style')
  expect(root).not.toHaveAttribute('data-appearance-transition')
})
