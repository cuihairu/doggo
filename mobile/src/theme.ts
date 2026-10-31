import { useColorScheme } from 'react-native'
import { useSyncExternalStore } from 'react'
import AsyncStorage from '@react-native-async-storage/async-storage'

export interface Palette {
  bg: string
  card: string
  text: string
  muted: string
  border: string
  primary: string
  primaryStrong: string
  primaryText: string
  danger: string
  success: string
  gold: string
}

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

/** 'system' restores the default behavior: follow the OS light/dark setting. */
export type ThemeChoice = ThemeId | 'system'

// Color values are the canonical tokens from docs/design/prototypes/tokens.css;
// only the fields the RN palette needs are carried over.
export const THEME_PALETTES: Record<ThemeId, Palette> = {
  brand: { bg: '#f5f6f8', card: '#ffffff', text: '#1f2329', muted: '#646a73', border: '#dee0e3', primary: '#2f66d0', primaryStrong: '#2f66d0', primaryText: '#ffffff', danger: '#d83931', success: '#2e9e6b', gold: '#c7893b' },
  dark: { bg: '#121417', card: '#1b1f24', text: '#e6e9ec', muted: '#9aa3ad', border: '#333b45', primary: '#6a96f0', primaryStrong: '#709af1', primaryText: '#0e1116', danger: '#ef5350', success: '#4caf7d', gold: '#e0b069' },
  cny: { bg: '#fff5f2', card: '#ffffff', text: '#3a1214', muted: '#a35a4d', border: '#f3d3c8', primary: '#c62828', primaryStrong: '#c62828', primaryText: '#ffffff', danger: '#e2503f', success: '#2f9e6b', gold: '#c7893b' },
  lantern: { bg: '#fff8f4', card: '#fffdfb', text: '#4a2a1c', muted: '#8d6752', border: '#f0dccf', primary: '#e06a3c', primaryStrong: '#b2461c', primaryText: '#ffffff', danger: '#d8433a', success: '#3f9e6b', gold: '#d19a2f' },
  dragonboat: { bg: '#eef6f3', card: '#ffffff', text: '#123c30', muted: '#517669', border: '#cfe2da', primary: '#0f7a5c', primaryStrong: '#0f7659', primaryText: '#ffffff', danger: '#d8543f', success: '#2e9e6b', gold: '#b8862f' },
  midautumn: { bg: '#101626', card: '#1a2236', text: '#eef1f8', muted: '#93a0bd', border: '#2f3a55', primary: '#e3b548', primaryStrong: '#e3b548', primaryText: '#1a2236', danger: '#f2726a', success: '#7fd0a8', gold: '#f0c674' },
  national: { bg: '#fff7f5', card: '#ffffff', text: '#3d1414', muted: '#98645a', border: '#f5d5cc', primary: '#d8322a', primaryStrong: '#c52c24', primaryText: '#ffffff', danger: '#e2503f', success: '#2f9e6b', gold: '#d4a017' },
  spring: { bg: '#fdf6f9', card: '#ffffff', text: '#33262e', muted: '#876a79', border: '#f2dde8', primary: '#e0688f', primaryStrong: '#c2285a', primaryText: '#ffffff', danger: '#d8433a', success: '#4ea87a', gold: '#c9a227' },
  summer: { bg: '#f0fafb', card: '#ffffff', text: '#123c44', muted: '#50777e', border: '#cbe7eb', primary: '#0f9bb0', primaryStrong: '#0b7585', primaryText: '#ffffff', danger: '#d8433a', success: '#3fa07d', gold: '#e0912f' },
  autumn: { bg: '#fbf5ec', card: '#fffdf9', text: '#3c2a18', muted: '#886940', border: '#ecdcc4', primary: '#b4622a', primaryStrong: '#9d5525', primaryText: '#ffffff', danger: '#d8433a', success: '#4f9e6b', gold: '#c9912b' },
  winter: { bg: '#f3f6f9', card: '#ffffff', text: '#1c2833', muted: '#5f717e', border: '#d8e2ea', primary: '#3d7ea6', primaryStrong: '#367094', primaryText: '#ffffff', danger: '#d8433a', success: '#4f9e7d', gold: '#b98b3c' }
}

const THEME_KEY = 'doggo.theme'

let override: ThemeId | null = null
const listeners = new Set<() => void>()

function notify() {
  for (const listener of listeners) listener()
}

function subscribe(onChange: () => void) {
  listeners.add(onChange)
  return () => {
    listeners.delete(onChange)
  }
}

function isThemeId(value: unknown): value is ThemeId {
  return typeof value === 'string' && THEMES.some((theme) => theme.id === value)
}

function paletteFor(scheme: 'light' | 'dark' | null | undefined, choice: ThemeChoice): Palette {
  if (choice !== 'system') return THEME_PALETTES[choice]
  return scheme === 'dark' ? THEME_PALETTES.dark : THEME_PALETTES.brand
}

/** Current choice; 'system' until a theme is picked or a persisted one is hydrated. */
export function themeChoice(): ThemeChoice {
  return override ?? 'system'
}

/** Persists the choice; 'system' clears the override and resumes following the OS scheme. */
export function setThemeChoice(choice: ThemeChoice): void {
  override = choice === 'system' ? null : choice
  AsyncStorage.setItem(THEME_KEY, JSON.stringify(choice)).catch(() => {})
  notify()
}

/**
 * Restores the persisted choice; call once at startup before the first render.
 * A corrupt value is ignored and the app stays on system follow.
 */
export async function hydrateTheme(): Promise<void> {
  try {
    const raw = await AsyncStorage.getItem(THEME_KEY)
    const value: unknown = raw ? JSON.parse(raw) : null
    if (isThemeId(value)) {
      override = value
      notify()
    }
  } catch {
    // Unreadable storage: stay on system follow.
  }
}

/** The active palette; re-renders on both manual picks and OS scheme flips. */
export function useTheme(): Palette {
  const scheme = useColorScheme()
  const choice = useSyncExternalStore(subscribe, themeChoice)
  return paletteFor(scheme, choice)
}

/** Current choice plus its setter, for theme pickers. */
export function useThemeChoice(): { choice: ThemeChoice; setChoice: (choice: ThemeChoice) => void } {
  const choice = useSyncExternalStore(subscribe, themeChoice)
  return { choice, setChoice: setThemeChoice }
}
