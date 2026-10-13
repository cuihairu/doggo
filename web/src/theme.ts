import { useSyncExternalStore } from 'react'

/**
 * 主题切换（docs/design/ui-system.md §4）：优先级 URL 片段 #theme=xxx > localStorage > brand。
 * 只改 <html data-theme>，布局不变。实现期与原型 theme.js 的一处差异：切换不写回 hash
 * （原型写 hash 是为了截图可复现），改用 localStorage 持久化，避免应用内导航污染 URL。
 */
export const THEMES = [
  { id: 'brand', label: '默认（品牌）' },
  { id: 'dark', label: '暗色' },
  { id: 'cny', label: '节日·春节' },
  { id: 'lantern', label: '节日·元宵' },
  { id: 'dragonboat', label: '节日·端午' },
  { id: 'midautumn', label: '节日·中秋' },
  { id: 'national', label: '节日·国庆' },
  { id: 'spring', label: '季节·春' },
  { id: 'summer', label: '季节·夏' },
  { id: 'autumn', label: '季节·秋' },
  { id: 'winter', label: '季节·冬' }
] as const

export type ThemeId = (typeof THEMES)[number]['id']

const THEME_KEY = 'doggo.theme'
const DEFAULT_THEME: ThemeId = 'brand'

const listeners = new Set<() => void>()
let snapshot: ThemeId = DEFAULT_THEME

export function isTheme(value: string | null | undefined): value is ThemeId {
  return THEMES.some((theme) => theme.id === value)
}

export function labelOf(theme: ThemeId): string {
  return THEMES.find((item) => item.id === theme)?.label ?? theme
}

function readHash(): ThemeId | null {
  const match = /[#&]theme=([a-z]+)/.exec(location.hash)
  const value = match?.[1]
  return isTheme(value) ? value : null
}

function resolve(): ThemeId {
  // 深链带来的选择先落 localStorage，SPA 导航丢掉 hash 后仍保留。
  const fromHash = readHash()
  if (fromHash) {
    localStorage.setItem(THEME_KEY, fromHash)
    return fromHash
  }
  const stored = localStorage.getItem(THEME_KEY)
  return isTheme(stored) ? stored : DEFAULT_THEME
}

/** Applies the current theme before React renders, so there is no brand flash. */
export function initTheme() {
  snapshot = resolve()
  document.documentElement.setAttribute('data-theme', snapshot)
  window.addEventListener('hashchange', () => {
    const next = readHash()
    if (next && next !== snapshot) {
      setTheme(next)
    }
  })
}

export function setTheme(theme: ThemeId) {
  document.documentElement.setAttribute('data-theme', theme)
  localStorage.setItem(THEME_KEY, theme)
  snapshot = theme
  for (const listener of listeners) {
    listener()
  }
}

export function useTheme() {
  const current = useSyncExternalStore(
    (onChange) => {
      listeners.add(onChange)
      return () => {
        listeners.delete(onChange)
      }
    },
    () => snapshot,
  )

  return { theme: current, themes: THEMES, setTheme }
}
