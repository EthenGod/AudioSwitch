import { memo, useMemo, useState } from 'react'
import { Marked } from 'marked'
import DOMPurify from 'dompurify'
import { nameToEmoji } from 'gemoji'
import { invoke, isTauri } from '@tauri-apps/api/core'

const markdown = new Marked({ gfm: true, walkTokens(token) {
  if (token.type === 'text' || token.type === 'escape') token.text = token.text.replace(/:([\w+-]+):/g, (source: string, name: string) => nameToEmoji[name] || source)
} })
export function safeWebUrl(value: string): string | null {
  try { const url = new URL(value); return ['https:', 'http:'].includes(url.protocol) && !url.username && !url.password ? url.href : null } catch { return null }
}
export function renderReleaseNotes(source: string): string {
  const html = markdown.parse(source, { async: false })
  const fragment = DOMPurify.sanitize(html, {
    ALLOWED_TAGS: ['p','br','hr','h1','h2','h3','h4','h5','h6','strong','b','em','i','del','s','blockquote','ul','ol','li','pre','code','a','img','table','thead','tbody','tr','th','td','details','summary','input','kbd','sup','sub'],
    ALLOWED_ATTR: ['href','src','alt','title','type','checked','disabled','start','align'],
    ALLOW_DATA_ATTR: false, ALLOW_ARIA_ATTR: false, RETURN_DOM_FRAGMENT: true,
  })
  for (const link of fragment.querySelectorAll('a')) {
    const url = safeWebUrl(link.getAttribute('href') || '')
    if (url) { link.setAttribute('href', url); link.setAttribute('target', '_blank'); link.setAttribute('rel', 'noopener noreferrer') }
    else link.removeAttribute('href')
  }
  for (const img of fragment.querySelectorAll('img')) {
    const url = safeWebUrl(img.getAttribute('src') || '')
    if (!url || !url.startsWith('https:')) { img.replaceWith(document.createTextNode(img.alt)); continue }
    img.src = url; img.setAttribute('loading', 'lazy'); img.setAttribute('referrerpolicy', 'no-referrer')
  }
  for (const input of fragment.querySelectorAll('input')) {
    if (input.type !== 'checkbox') input.remove()
    else { input.disabled = true; input.setAttribute('aria-label', input.checked ? '已完成' : '未完成') }
  }
  for (const table of fragment.querySelectorAll('table')) {
    const scroll = document.createElement('div'); scroll.className = 'release-table'; scroll.tabIndex = 0
    scroll.setAttribute('role', 'region'); scroll.setAttribute('aria-label', '更新说明表格')
    table.replaceWith(scroll); scroll.append(table)
  }
  const container = document.createElement('div'); container.append(fragment); return container.innerHTML
}
export const ReleaseNotes = memo(function ReleaseNotes({ source }: { source: string }) {
  const html = useMemo(() => renderReleaseNotes(source), [source])
  const [error, setError] = useState('')
  return <><div className="release-notes" onClick={event => {
    const link = (event.target as Element).closest('a[href]')
    if (link && isTauri()) {
      event.preventDefault(); setError('')
      void invoke('open_release_link', { url: link.getAttribute('href') }).catch(() => setError('无法打开链接，请复制链接到浏览器。'))
    }
  }} dangerouslySetInnerHTML={{ __html: html }} />{error && <p role="alert">{error}</p>}</>
})
