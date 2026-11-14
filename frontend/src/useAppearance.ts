import { useCallback, useLayoutEffect, useMemo, useSyncExternalStore } from 'react'
import { DEFAULT_UI_STYLE, getUiStyle } from './appearance/styles'
import { createAppearanceTransition } from './appearance/transition'

// Only standalone errors follow the OS. A panel with a saved theme needs no listener.
export function useAppearance(preferred: boolean | undefined, style: string = DEFAULT_UI_STYLE, ready = true) {
  const appearance = useMemo(() => createAppearanceTransition(), [])
  const resolvedStyle = getUiStyle(style).id
  const followSystem = preferred === undefined
  const media = useMemo(() => {
    if (!followSystem) return null
    try { return window.matchMedia('(prefers-color-scheme: light)') } catch { return null }
  }, [followSystem])
  const subscribe = useCallback((listener: () => void) => {
    media?.addEventListener('change', listener)
    return () => media?.removeEventListener('change', listener)
  }, [media])
  const dark = useSyncExternalStore(subscribe, () => preferred ?? !media?.matches)
  useLayoutEffect(() => () => appearance.dispose(), [appearance])
  useLayoutEffect(() => {
    appearance.update({ dark, style: resolvedStyle }, ready)
  }, [appearance, dark, resolvedStyle, ready])
  return dark
}
