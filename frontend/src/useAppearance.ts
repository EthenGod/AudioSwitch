import { useCallback, useLayoutEffect, useMemo, useSyncExternalStore } from 'react'

// Only standalone errors follow the OS. A panel with a saved theme needs no listener.
export function useAppearance(preferred: boolean | undefined) {
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
  useLayoutEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
  }, [dark])
  return dark
}
