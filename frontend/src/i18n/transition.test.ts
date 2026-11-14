import { afterEach, expect, it, vi } from 'vitest'
import { createLanguageTransition } from './transition'

const controllers: ReturnType<typeof createLanguageTransition>[] = []
const create = (apply = vi.fn(), capture = apply) => {
  const controller = createLanguageTransition(apply, capture)
  controllers.push(controller)
  return controller
}
afterEach(() => {
  controllers.splice(0).forEach(value => value.dispose())
  vi.restoreAllMocks()
  Reflect.deleteProperty(document, 'startViewTransition')
  Reflect.deleteProperty(document.documentElement, 'animate')
  delete document.documentElement.dataset.appearanceTransition
})

function captures() {
  const pending: { commit: () => void; finish: () => void; reject: () => void; skipTransition: ReturnType<typeof vi.fn> }[] = []
  const start = vi.fn((commit: () => void) => {
    let finish!: () => void, reject!: () => void
    const finished = new Promise<void>((resolve, fail) => { finish = resolve; reject = () => fail(new Error('capture failed')) })
    const skipTransition = vi.fn()
    pending.push({ commit, finish, reject, skipTransition })
    return { ready: Promise.resolve(), finished, skipTransition }
  })
  Object.defineProperty(document, 'startViewTransition', { configurable: true, value: start })
  return { start, pending }
}

it('does not capture initial load or repeated snapshots, and waits for a confirmed preference', () => {
  const { start } = captures(), apply = vi.fn(), controller = create(apply)
  controller.update('en', false)
  expect(apply).not.toHaveBeenCalled()
  controller.update('ja')
  const scan = vi.spyOn(document, 'querySelectorAll')
  for (let i = 0; i < 100; i++) controller.update('ja')
  expect(apply).toHaveBeenCalledExactlyOnceWith('ja')
  expect(scan).not.toHaveBeenCalled()
  expect(start).not.toHaveBeenCalled()
})

it('captures visible surfaces including portals, preserves controls, and restores temporary names', async () => {
  const { pending } = captures(), apply = vi.fn(), commit = vi.fn(), controller = create(apply, commit)
  const sheet = document.createElement('section'), input = document.createElement('input')
  sheet.className = 'sheet-content'; sheet.style.viewTransitionName = 'existing-name'
  sheet.append(input); document.body.append(sheet)
  vi.spyOn(sheet, 'getBoundingClientRect').mockReturnValue({ left: 200, top: 20, right: 600, bottom: 600, width: 400, height: 580 } as DOMRect)
  input.value = 'unsaved device name'; input.focus(); sheet.scrollTop = 51
  try {
    controller.update('zh-CN'); controller.update('en')
    expect(sheet.style.viewTransitionName).toMatch(/^language-/)
    expect(commit).not.toHaveBeenCalled()
    pending[0].commit()
    expect(commit).toHaveBeenCalledExactlyOnceWith('en')
    expect(sheet.firstChild).toBe(input); expect(input).toHaveFocus()
    expect(input.value).toBe('unsaved device name'); expect(sheet.scrollTop).toBe(51)
    pending[0].finish(); await Promise.resolve()
    expect(sheet.style.viewTransitionName).toBe('existing-name')
    expect(document.documentElement).not.toHaveAttribute('data-language-transition')
  } finally { sheet.remove() }
})

it('ignores stale callbacks on rapid language changes, unmount and StrictMode setup', async () => {
  const { pending } = captures(), apply = vi.fn(), controller = create(apply)
  controller.update('zh-CN'); controller.update('en'); controller.update('ja')
  expect(pending[0].skipTransition).toHaveBeenCalledOnce()
  pending[1].commit(); pending[0].commit(); pending[0].finish(); await Promise.resolve()
  expect(apply.mock.calls.map(args => args[0])).toEqual(['zh-CN', 'ja'])
  expect(document.documentElement).toHaveAttribute('data-language-transition')
  controller.dispose(); pending[1].commit(); pending[1].finish(); await Promise.resolve()
  expect(document.documentElement).not.toHaveAttribute('data-language-transition')
  controller.update('en')
  expect(apply).toHaveBeenLastCalledWith('en')
  expect(pending).toHaveLength(2)
})

it('skips animation for reduced motion, hidden windows and an active appearance transition', () => {
  const { start } = captures(), apply = vi.fn(), controller = create(apply)
  controller.update('zh-CN')
  const media = window.matchMedia
  vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ ...media(query), matches: true }))
  controller.update('en')
  vi.restoreAllMocks()
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
  controller.update('ja')
  vi.restoreAllMocks()
  document.documentElement.dataset.appearanceTransition = ''
  controller.update('zh-CN')
  expect(start).not.toHaveBeenCalled()
  expect(apply.mock.calls.map(args => args[0])).toEqual(['zh-CN', 'en', 'ja', 'zh-CN'])
})

it('still applies the selection on failed captures and cleans up animation state', async () => {
  const { pending } = captures(), apply = vi.fn(), controller = create(apply)
  controller.update('zh-CN'); controller.update('en')
  pending[0].reject(); await Promise.resolve()
  expect(apply).toHaveBeenLastCalledWith('en')
  expect(document.documentElement).not.toHaveAttribute('data-language-transition')
  pending[0].commit()
  expect(apply).toHaveBeenCalledTimes(2)
})

it('provides an interruptible fallback when view transitions are unavailable', async () => {
  const fades: { finish: () => void; cancel: ReturnType<typeof vi.fn> }[] = []
  const animate = vi.fn(() => {
    let finish!: () => void
    const finished = new Promise<void>(resolve => { finish = resolve }), cancel = vi.fn(finish)
    fades.push({ finish, cancel })
    return { finished, cancel }
  })
  Object.defineProperty(document.documentElement, 'animate', { configurable: true, value: animate })
  const apply = vi.fn(), controller = create(apply)
  controller.update('zh-CN'); controller.update('en')
  expect(apply).toHaveBeenLastCalledWith('zh-CN')
  expect(animate).toHaveBeenCalledOnce()
  expect(document.documentElement).toHaveAttribute('data-language-transition')
  fades[0].finish(); await Promise.resolve()
  expect(apply).toHaveBeenLastCalledWith('en')
  expect(animate).toHaveBeenCalledTimes(2)
  controller.dispose(); await Promise.resolve()
  expect(fades[1].cancel).toHaveBeenCalledOnce()
  expect(document.documentElement).not.toHaveAttribute('data-language-transition')
})
