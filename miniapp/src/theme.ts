import { useSyncExternalStore } from 'react'
import Taro from '@tarojs/taro'

/** The 11 themes from docs/design/ui-system.md §4; ids and labels match the web source. */
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

/** Primary color per theme, for picker swatches. */
export const THEME_SWATCH: Record<ThemeId, string> = {
  brand: '#2f66d0',
  dark: '#6a96f0',
  cny: '#c62828',
  lantern: '#e06a3c',
  dragonboat: '#0f7a5c',
  midautumn: '#e3b548',
  national: '#d8322a',
  spring: '#e0688f',
  summer: '#0f9bb0',
  autumn: '#b4622a',
  winter: '#3d7ea6'
}

// Only the values that must be set through the native API (page background,
// navigation bar) are kept here; the rest live in app.scss as CSS variables.
const NATIVE: Record<ThemeId, { bg: string; bar: string; barText: string }> = {
  brand: { bg: '#f5f6f8', bar: '#2f66d0', barText: '#ffffff' },
  dark: { bg: '#121417', bar: '#6a96f0', barText: '#0e1116' },
  cny: { bg: '#fff5f2', bar: '#c62828', barText: '#ffffff' },
  lantern: { bg: '#fff8f4', bar: '#e06a3c', barText: '#ffffff' },
  dragonboat: { bg: '#eef6f3', bar: '#0f7a5c', barText: '#ffffff' },
  midautumn: { bg: '#101626', bar: '#e3b548', barText: '#1a2236' },
  national: { bg: '#fff7f5', bar: '#d8322a', barText: '#ffffff' },
  spring: { bg: '#fdf6f9', bar: '#e0688f', barText: '#ffffff' },
  summer: { bg: '#f0fafb', bar: '#0f9bb0', barText: '#ffffff' },
  autumn: { bg: '#fbf5ec', bar: '#b4622a', barText: '#ffffff' },
  winter: { bg: '#f3f6f9', bar: '#3d7ea6', barText: '#ffffff' }
}

const THEME_KEY = 'doggo.theme'
const DEFAULT_THEME: ThemeId = 'brand'

let current: ThemeId = DEFAULT_THEME
const listeners = new Set<() => void>()

function subscribe(onChange: () => void) {
  listeners.add(onChange)
  return () => {
    listeners.delete(onChange)
  }
}

function isThemeId(value: unknown): value is ThemeId {
  return typeof value === 'string' && THEMES.some((theme) => theme.id === value)
}

/** Native chrome cannot read CSS variables, so sync it on every change. */
function applyNative(theme: ThemeId) {
  const next = NATIVE[theme]
  Taro.setBackgroundColor({ backgroundColor: next.bg }).catch(() => {})
  Taro.setNavigationBarColor({ frontColor: next.barText, backgroundColor: next.bar }).catch(() => {})
}

export function getTheme(): ThemeId {
  return current
}

/** Persists the pick and applies it to the CSS variables (via ThemeRoot) and native chrome. */
export function setTheme(theme: ThemeId) {
  current = theme
  Taro.setStorageSync(THEME_KEY, theme)
  applyNative(theme)
  for (const listener of listeners) listener()
}

/** Reads the persisted pick and applies native chrome before the first render. */
export function initTheme() {
  const stored = Taro.getStorageSync(THEME_KEY)
  if (isThemeId(stored)) current = stored
  applyNative(current)
}

export function useTheme(): ThemeId {
  return useSyncExternalStore(subscribe, getTheme)
}
