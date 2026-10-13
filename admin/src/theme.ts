import { useSyncExternalStore } from 'react'
import { theme as antdTheme, type ThemeConfig } from 'antd'

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

/** Primary color per theme, for dropdown swatches. */
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

// Color values are the canonical tokens from docs/design/prototypes/tokens.css.
interface Palette {
  bg: string
  surface: string
  text: string
  muted: string
  border: string
  primary: string
  danger: string
  success: string
  gold: string
  accent2: string
  radius: number
}

const PALETTES: Record<ThemeId, Palette> = {
  brand: { bg: '#f5f6f8', surface: '#ffffff', text: '#1f2329', muted: '#646a73', border: '#dee0e3', primary: '#2f66d0', danger: '#d83931', success: '#2e9e6b', gold: '#c7893b', accent2: '#5b8def', radius: 8 },
  dark: { bg: '#121417', surface: '#1b1f24', text: '#e6e9ec', muted: '#9aa3ad', border: '#333b45', primary: '#6a96f0', danger: '#ef5350', success: '#4caf7d', gold: '#e0b069', accent2: '#7c9bff', radius: 10 },
  cny: { bg: '#fff5f2', surface: '#ffffff', text: '#3a1214', muted: '#a35a4d', border: '#f3d3c8', primary: '#c62828', danger: '#e2503f', success: '#2f9e6b', gold: '#c7893b', accent2: '#d99a2b', radius: 10 },
  lantern: { bg: '#fff8f4', surface: '#fffdfb', text: '#4a2a1c', muted: '#a97b62', border: '#f0dccf', primary: '#e06a3c', danger: '#d8433a', success: '#3f9e6b', gold: '#d19a2f', accent2: '#e8a44c', radius: 10 },
  dragonboat: { bg: '#eef6f3', surface: '#ffffff', text: '#123c30', muted: '#5c8577', border: '#cfe2da', primary: '#0f7a5c', danger: '#d8543f', success: '#2e9e6b', gold: '#b8862f', accent2: '#3d9a7a', radius: 10 },
  midautumn: { bg: '#101626', surface: '#1a2236', text: '#eef1f8', muted: '#93a0bd', border: '#2f3a55', primary: '#e3b548', danger: '#f2726a', success: '#7fd0a8', gold: '#f0c674', accent2: '#8fa4d6', radius: 10 },
  national: { bg: '#fff7f5', surface: '#ffffff', text: '#3d1414', muted: '#a06a5f', border: '#f5d5cc', primary: '#d8322a', danger: '#e2503f', success: '#2f9e6b', gold: '#d4a017', accent2: '#e0912c', radius: 10 },
  spring: { bg: '#fdf6f9', surface: '#ffffff', text: '#33262e', muted: '#8a6d7c', border: '#f2dde8', primary: '#e0688f', danger: '#d8433a', success: '#4ea87a', gold: '#c9a227', accent2: '#7fc4a0', radius: 12 },
  summer: { bg: '#f0fafb', surface: '#ffffff', text: '#123c44', muted: '#5b8890', border: '#cbe7eb', primary: '#0f9bb0', danger: '#d8433a', success: '#3fa07d', gold: '#e0912f', accent2: '#4bbcd4', radius: 12 },
  autumn: { bg: '#fbf5ec', surface: '#fffdf9', text: '#3c2a18', muted: '#997748', border: '#ecdcc4', primary: '#b4622a', danger: '#d8433a', success: '#4f9e6b', gold: '#c9912b', accent2: '#cf9a4a', radius: 10 },
  winter: { bg: '#f3f6f9', surface: '#ffffff', text: '#1c2833', muted: '#6b7f8e', border: '#d8e2ea', primary: '#3d7ea6', danger: '#d8433a', success: '#4f9e7d', gold: '#b98b3c', accent2: '#6ea6c4', radius: 10 }
}

// Long-running shifts favor a dark scheme (ui-system.md §7): mid-autumn is a dark theme too.
const DARK_THEMES: ReadonlySet<ThemeId> = new Set<ThemeId>(['dark', 'midautumn'])

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

export function getTheme(): ThemeId {
  return current
}

/** Persists the pick; the ConfigProvider wrapper and the topbar dropdown re-theme at once. */
export function setTheme(theme: ThemeId) {
  current = theme
  localStorage.setItem(THEME_KEY, theme)
  document.documentElement.setAttribute('data-theme', theme)
  for (const listener of listeners) listener()
}

/** Reads the persisted pick before the first render (call from initTheme). */
export function initTheme() {
  const stored = localStorage.getItem(THEME_KEY)
  if (isThemeId(stored)) current = stored
  document.documentElement.setAttribute('data-theme', current)
}

export function useTheme(): ThemeId {
  return useSyncExternalStore(subscribe, getTheme)
}

/** antd token mapping (ui-system.md §8): colors collapse into ConfigProvider tokens. */
export function antdThemeFor(id: ThemeId): ThemeConfig {
  const p = PALETTES[id]
  return {
    algorithm: DARK_THEMES.has(id) ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm,
    token: {
      colorPrimary: p.primary,
      colorInfo: p.accent2,
      colorSuccess: p.success,
      colorWarning: p.gold,
      colorError: p.danger,
      colorTextBase: p.text,
      colorBgBase: p.bg,
      colorBgContainer: p.surface,
      colorBgElevated: p.surface,
      colorBgLayout: p.bg,
      colorBorder: p.border,
      colorBorderSecondary: p.border,
      colorTextSecondary: p.muted,
      colorTextTertiary: p.muted,
      borderRadius: p.radius
    }
  }
}
