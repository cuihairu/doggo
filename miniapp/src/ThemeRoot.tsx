import type { ReactNode } from 'react'
import { View } from '@tarojs/components'
import { useTheme } from './theme'

/**
 * Small-program pages have no html element, so each page root writes
 * data-theme itself for the CSS variables to scope from (app.scss).
 */
export default function ThemeRoot({ children }: { children: ReactNode }) {
  const theme = useTheme()
  return (
    <View data-theme={theme} className="app-root" style={{ minHeight: '100vh' }}>
      {children}
    </View>
  )
}
