import type { ReactNode } from 'react'
import type { RequestConfig, RunTimeLayoutConfig } from '@umijs/max'
import { history } from '@umijs/max'
import ThemeGate, { ThemeSwitch } from './ThemeGate'
import { getTheme, initTheme } from './theme'

// Reads the persisted pick before the first render so no wrong theme flashes.
initTheme()

const ADMIN_TOKEN_KEY = 'doggo.admin.accessToken'

export function getToken(): string | null {
  return localStorage.getItem(ADMIN_TOKEN_KEY)
}

export function setToken(token: string) {
  localStorage.setItem(ADMIN_TOKEN_KEY, token)
}

export function clearToken() {
  localStorage.removeItem(ADMIN_TOKEN_KEY)
}

let requestConfigRef: RequestConfig = {}

export const request: RequestConfig = {
  baseURL: '',
  requestInterceptors: [
    (url, options) => {
      const token = getToken()
      options.headers = {
        ...options.headers,
        // Funnel design §9.3: server-side visit logs and events split per client.
        'X-Doggo-Client': 'admin'
      }
      if (token) {
        options.headers = { ...options.headers, Authorization: `Bearer ${token}` }
      }
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
  if (!getToken()) {
    return { isAdmin: false }
  }
  try {
    const response = await fetch('/api/auth/me', {
      headers: { Authorization: `Bearer ${getToken()}` },
    })
    if (!response.ok) {
      clearToken()
      return { isAdmin: false }
    }
    const profile = await response.json()
    // Role lives in the access token payload; decode it without a library.
    const payload = JSON.parse(atob(getToken()!.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    const isAdmin = payload.role === 'admin'
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
