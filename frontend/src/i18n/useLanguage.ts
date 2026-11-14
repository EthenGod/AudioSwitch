import { useLayoutEffect, useMemo, useSyncExternalStore } from 'react'
import { flushSync } from 'react-dom'
import { getLanguage, resolveLanguage, setLanguage, subscribeLanguage } from './index'
import { createLanguageTransition } from './transition'

/** Keeps independent panel/prompt windows in sync with confirmed preferences. */
export function useLanguage(saved?: string | null, recommended?: string, ready = true) {
  const language = useSyncExternalStore(subscribeLanguage, getLanguage, getLanguage)
  const transition = useMemo(() => createLanguageTransition(setLanguage, next => flushSync(() => setLanguage(next))), [])
  useLayoutEffect(() => () => transition.dispose(), [transition])
  useLayoutEffect(() => {
    transition.update(resolveLanguage(saved, recommended), ready)
  }, [saved, recommended, ready, transition])
  return language
}
