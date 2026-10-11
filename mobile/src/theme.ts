import { useColorScheme } from 'react-native'

export interface Palette {
  bg: string
  card: string
  text: string
  muted: string
  border: string
  primary: string
  primaryText: string
  danger: string
}

const light: Palette = {
  bg: '#f6f7f9',
  card: '#ffffff',
  text: '#1a1a1a',
  muted: '#8a8f99',
  border: '#e5e7eb',
  primary: '#2f6fed',
  primaryText: '#ffffff',
  danger: '#c0392b'
}

const dark: Palette = {
  bg: '#111318',
  card: '#1b1e26',
  text: '#f2f3f5',
  muted: '#9aa1ad',
  border: '#2a2e38',
  primary: '#4d7ff0',
  primaryText: '#ffffff',
  danger: '#e06c5a'
}

/** Follows the system color scheme (mobile hard requirement: dark-mode follow). */
export function useTheme(): Palette {
  const scheme = useColorScheme()
  return scheme === 'dark' ? dark : light
}
