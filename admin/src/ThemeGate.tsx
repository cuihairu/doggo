import type { ReactNode } from 'react'
import { ConfigProvider, Select } from 'antd'
import { antdThemeFor, setTheme, THEMES, useTheme, type ThemeId } from './theme'

/**
 * Re-themes the whole app from the theme store; umi's rootContainer wraps this
 * around everything, so a pick re-renders ConfigProvider with new tokens.
 */
export default function ThemeGate({ children }: { children: ReactNode }) {
  const theme = useTheme()
  return <ConfigProvider theme={antdThemeFor(theme)}>{children}</ConfigProvider>
}

/** Topbar dropdown (prototype puts the theme picker in the header). */
export function ThemeSwitch() {
  const theme = useTheme()
  return (
    <Select
      aria-label="主题"
      size="small"
      variant="borderless"
      value={theme}
      onChange={(value) => setTheme(value as ThemeId)}
      options={THEMES.map((item) => ({ value: item.id, label: item.label }))}
      style={{ width: 136 }}
    />
  )
}
