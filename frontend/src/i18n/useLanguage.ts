import { useLayoutEffect, useMemo, useSyncExternalStore } from 'react'
import { flushSync } from 'react-dom'
import { getLanguage, resolveLanguage, setLanguage, subscribeLanguage, subscribeSystemLanguage, systemLanguage, SYSTEM_LANGUAGE } from './index'
import { createLanguageTransition } from './transition'

const noSubscription = () => () => {}

/** Keeps independent panel/prompt windows in sync with confirmed preferences. */
export function useLanguage(saved?: string | null, recommended?: string, ready = true) {
  const language = useSyncExternalStore(subscribeLanguage, getLanguage, getLanguage)
  // Desktop snapshots use Windows' language; offline previews use browser notifications.
  const system = useSyncExternalStore(!recommended && (!saved || saved === SYSTEM_LANGUAGE) ? subscribeSystemLanguage : noSubscription, systemLanguage, systemLanguage)
  const transition = useMemo(() => createLanguageTransition(setLanguage, next => flushSync(() => setLanguage(next))), [])
  useLayoutEffect(() => () => transition.dispose(), [transition])
  useLayoutEffect(() => {
    transition.update(resolveLanguage(saved, recommended ?? system), ready)
  }, [saved, recommended, system, ready, transition])
  return language
}
