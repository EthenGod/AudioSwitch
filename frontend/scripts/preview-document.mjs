/** Last template operation: never search the resulting document for structural HTML tags. */
export function inlinePreviewScript(html, code) {
  let count = 0
  const result = html.replace(/<script\b[^>]*\bsrc="[^"]+"[^>]*><\/script>/g, () => {
    count++
    return `<script type="module">${code.replace(/<\/script/gi, '<\\/script')}</script>`
  })
  if (count !== 1) throw new Error('预览脚本数量不符合预期。')
  return result
}
