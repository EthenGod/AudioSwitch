/** Stable IDs and semantic tokens only. Native tray palettes live in src/PopupTheme.cs. */
export const DEFAULT_UI_STYLE = 'classic'
const classic = { id: DEFAULT_UI_STYLE, label: '经典' }
export const UI_STYLES: readonly { readonly id: string; readonly label: string }[] = [
  classic,
]

export function isUiStyle(value: unknown): value is string {
  return UI_STYLES.some(style => style.id === value)
}

/** The wire stores an opaque, bounded ID so new styles need no backend changes. */
export function isUiStyleId(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= 32
    && /^[a-z]/u.test(value) && !/[^a-z0-9-]/u.test(value)
}

// Preserve an unrecognised saved ID in the snapshot; only its visual falls back.
export function getUiStyle(id: string = DEFAULT_UI_STYLE) {
  return UI_STYLES.find(style => style.id === id) ?? classic
}
