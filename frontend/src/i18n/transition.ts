// Named snapshots animate each visible boundary without remounting controls.
// The selector list is limited to layout surfaces, not every translated text node.
const surfaces = [
  '.sidebar', '.preview-banner', '.page-heading', '.settings-group', '.setting-row',
  '.section-heading', '.summary-card', '.device-section', '.priority-list',
  '.maintenance-item', '.about-line', '.statusbar', '.sheet-content', '.sheet-header',
  '.sheet-footer', '.prompt-shell', '.prompt-header', '.prompt-event', '.prompt-actions',
  '.language-dialog', '.toast', '[data-language-motion]',
].join(',')

export function createLanguageTransition(
  apply: (language: string) => void,
  // View-transition callbacks run outside React's commit, so the hook flushes there.
  captureCommit = apply,
  doc: Document = document,
) {
  const root = doc.documentElement
  let previous: string | undefined
  let generation = 0
  let transition: ViewTransition | undefined
  let fallback: Animation | undefined
  let restore: (() => void) | undefined

  function stop() {
    generation++
    transition?.skipTransition(); transition = undefined
    fallback?.cancel(); fallback = undefined
    restore?.(); restore = undefined
    delete root.dataset.languageTransition
  }

  function nameSurfaces() {
    const saved: [HTMLElement, string][] = []
    const view = doc.defaultView!
    for (const element of doc.querySelectorAll<HTMLElement>(surfaces)) {
      if (element.closest('[inert], [aria-hidden=true]')) continue
      const box = element.getBoundingClientRect()
      if (!box.width || !box.height || box.bottom <= 0 || box.right <= 0 || box.top >= view.innerHeight || box.left >= view.innerWidth) continue
      saved.push([element, element.style.viewTransitionName])
    }
    restore = () => { for (const [element, name] of saved) element.style.viewTransitionName = name }
    // Finish layout reads before writing styles, so each surface uses the same layout.
    saved.forEach(([element], index) => { element.style.viewTransitionName = `language-${index + 1}` })
  }

  return {
    update(next: string, ready = true) {
      if (!ready || next === previous) return
      let reduced = true
      try { reduced = doc.defaultView!.matchMedia('(prefers-reduced-motion: reduce)').matches } catch { /* Apply immediately. */ }
      const animate = previous !== undefined && !reduced && doc.visibilityState === 'visible' && !root.hasAttribute('data-appearance-transition')
      previous = next
      stop()
      const version = generation
      let committed = false
      const commit = (capture = false) => {
        if (version !== generation || committed) return
        committed = true
        ;(capture ? captureCommit : apply)(next)
      }
      const finish = () => {
        if (version !== generation) return
        transition = undefined; fallback = undefined
        restore?.(); restore = undefined
        delete root.dataset.languageTransition
      }
      if (!animate) { commit(); return }
      if (typeof doc.startViewTransition === 'function') {
        try {
          nameSurfaces()
          root.dataset.languageTransition = ''
          transition = doc.startViewTransition(() => commit(true))
          void transition.ready.catch(() => {})
          void transition.finished.then(finish, () => { commit(); finish() })
          return
        } catch { finish() }
      }
      // Without snapshots, change text/layout only after the old text has faded out.
      // Keep the actual DOM, focus and input state; no cloned tree or permanent layer.
      if (typeof root.animate !== 'function') { commit(); return }
      root.dataset.languageTransition = ''
      fallback = root.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 200, easing: 'ease-in', fill: 'forwards' })
      void fallback.finished.then(() => {
        if (version !== generation) return
        commit(true)
        fallback?.cancel()
        fallback = root.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 250, easing: 'ease-out' })
        void fallback.finished.then(finish, finish)
      }, () => { if (version === generation) { commit(true); finish() } })
    },
    dispose() { stop(); previous = undefined },
  }
}
