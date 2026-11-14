import { useLayoutEffect, useSyncExternalStore } from 'react'
import { getLanguage, resolveLanguage, setLanguage, subscribeLanguage } from './index'

/** Keeps independent panel/prompt windows in sync with confirmed preferences. */
export function useLanguage(saved?: string | null, recommended?: string, ready = true) {
  const language = useSyncExternalStore(subscribeLanguage, getLanguage, getLanguage)
  useLayoutEffect(() => {
    if (ready) setLanguage(resolveLanguage(saved, recommended))
  }, [saved, recommended, ready])
  return language
}
