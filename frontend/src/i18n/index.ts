import languages from '../../../locales/languages.json'

// Source Chinese is the message key and the final fallback. Catalogues are data,
// never HTML or executable modules. All surfaces use the same resources.
const files = import.meta.glob<Record<string, string>>('../../../locales/*.json', { eager: true, import: 'default' })
export const LANGUAGES = languages
export const DEFAULT_LANGUAGE = 'zh-CN'
export function isLanguageId(value: unknown): value is string {
  return typeof value === 'string' && /^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/.test(value) && value.length <= 35
}
export function matchLanguage(preferred: readonly string[]): string {
  for (const value of preferred) {
    const tag = value.toLowerCase().replaceAll('_', '-')
    const exact = languages.find(language => language.id.toLowerCase() === tag)
    if (exact) return exact.id
    const match = languages.find(language => language.match.some(prefix => tag === prefix || tag.startsWith(prefix + '-')))
    if (match) return match.id
  }
  return DEFAULT_LANGUAGE
}
export function resolveLanguage(saved?: string | null, recommended?: string): string {
  if (saved) return languages.find(language => language.id === saved)?.id ?? DEFAULT_LANGUAGE
  return matchLanguage(recommended ? [recommended] : typeof navigator === 'undefined' ? [] : navigator.languages)
}
let language = DEFAULT_LANGUAGE
const listeners = new Set<() => void>()
export const getLanguage = () => language
export function subscribeLanguage(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } }
export function setLanguage(value: string) {
  const next = languages.some(item => item.id === value) ? value : DEFAULT_LANGUAGE
  if (next === language) return
  language = next
  if (typeof document !== 'undefined') document.documentElement.lang = next
  listeners.forEach(listener => listener())
}
export function translate(locale: string, source: string, ...args: unknown[]): string {
  const catalog = files[`../../../locales/${locale}.json`]
  const base = files['../../../locales/zh-CN.json']
  const translated = catalog && Object.hasOwn(catalog, source) ? catalog[source] : ''
  const message = translated || (base && Object.hasOwn(base, source) ? base[source] : source)
  return args.length ? message.replace(/\{(\d+)\}/g, (token, index: string) => Number(index) < args.length ? String(args[Number(index)] ?? '') : token) : message
}
export function t(source: string, ...args: unknown[]) { return translate(language, source, ...args) }
