import type { ReactNode } from 'react'
import type { RequestConfig, RunTimeLayoutConfig } from '@umijs/max'
import { history } from '@umijs/max'
import ThemeGate, { ThemeSwitch } from './ThemeGate'
import { getTheme, initTheme } from './theme'

// Reads the persisted pick before the first render so no wrong theme flashes.
initTheme()

export function clearToken() {}

let requestConfigRef: RequestConfig = {}

export const request: RequestConfig = {
  baseURL: '',
  requestInterceptors: [
    (url, options) => {
      options.headers = {
        ...options.headers,
        // Funnel design §9.3: server-side visit logs and events split per client.
        'X-Doggo-Client': 'admin'
      }
      options.credentials = 'same-origin'
      return { url, options }
    },
  ],
  responseInterceptors: [
    async (response) => {
      if (response.status === 401) {
        clearToken()
        history.push('/login')
      }
      return response
    },
  ],
}

requestConfigRef = request
void requestConfigRef

export async function getInitialState(): Promise<{ isAdmin: boolean; nickname?: string }> {
  try {
    const response = await fetch('/api/auth/me', { credentials: 'same-origin' })
    if (!response.ok) {
      clearToken()
      return { isAdmin: false }
    }
    const profile = await response.json()
    // Role is confirmed by the admin endpoint authorisation; /me only proves the
    // cookie session, so gate the initial state on the profile plus a role probe.
    const isAdmin = profile.isAdmin === true
    return { isAdmin, nickname: profile.nickname }
  } catch {
    return { isAdmin: false }
  }
}

export const layout: RunTimeLayoutConfig = ({ initialState }) => ({
  menuHeaderRender: undefined,
  // Sider follows the pick on the next navigation (the generated Layout only
  // re-evaluates this config when the route changes); page content re-themes at once.
  navTheme: getTheme() === 'dark' || getTheme() === 'midautumn' ? 'dark' : 'light',
  actionsRender: () => [<ThemeSwitch key="theme" />],
  onPageChange: () => {
    const { location } = history
    if (!initialState?.isAdmin && location.pathname !== '/login') {
      history.push('/login')
    }
  },
})

/** Wraps the app so every antd surface consumes the picked theme's tokens. */
export function rootContainer(container: ReactNode) {
  return <ThemeGate>{container}</ThemeGate>
}
