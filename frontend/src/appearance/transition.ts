export type Appearance = { dark: boolean; style: string }

/** One transient animation per document, never a second React tree or a polling loop. */
export function createAppearanceTransition(doc: Document = document) {
  const root = doc.documentElement
  let previous: Appearance | undefined
  let initialized = false
  let generation = 0
  let transition: ViewTransition | undefined
  let fallback: Animation | undefined

  function stop() {
    generation++
    transition?.skipTransition()
    transition = undefined
    fallback?.cancel()
    fallback = undefined
    delete root.dataset.appearanceTransition
  }

  function apply({ dark, style }: Appearance) {
    root.classList.toggle('dark', dark)
    root.style.colorScheme = dark ? 'dark' : 'light'
    // Portaled sheets, selects and tooltips inherit the same tokens.
    root.dataset.uiStyle = style
  }

  return {
    update(next: Appearance, ready = true) {
      const changed = !previous || next.dark !== previous.dark || next.style !== previous.style
      if (!changed && initialized === ready) return
      let reduced = true
      try { reduced = doc.defaultView!.matchMedia('(prefers-reduced-motion: reduce)').matches } catch { /* Immediate fallback. */ }
      const animate = initialized && ready && changed && !reduced && doc.visibilityState === 'visible'
      previous = next
      initialized = ready
      stop()
      const version = generation
      const commit = () => { if (version === generation) apply(next) }
      const finish = () => {
        if (version !== generation) return
        transition = undefined
        fallback = undefined
        delete root.dataset.appearanceTransition
      }
      if (!animate) { commit(); return }

      if (typeof doc.startViewTransition === 'function') {
        try {
          root.dataset.appearanceTransition = ''
          transition = doc.startViewTransition(commit)
          // Skipping an interrupted capture rejects ready, but still runs its callback.
          void transition.ready.catch(() => {})
          void transition.finished.then(finish, () => { commit(); finish() })
          return
        } catch { delete root.dataset.appearanceTransition }
      }

      // Older WebViews keep the same document and receive a short, restrained fade.
      commit()
      if (typeof root.animate === 'function') {
        fallback = root.animate([{ opacity: 0.88 }, { opacity: 1 }], { duration: 250, easing: 'ease-out' })
        void fallback.finished.then(finish, finish)
      }
    },
    dispose() { stop(); previous = undefined; initialized = false },
  }
}
