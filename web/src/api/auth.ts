import { api, clearTokens, saveTokens } from './client'

export interface AuthTokens {
  userId: string
  nickname: string
  accessToken: string
  accessTokenExpiresAtUtc: string
  refreshToken: string
}

export interface Profile {
  id: string
  phone: string
  email: string
  nickname: string
  avatarUrl: string
  inviteCode: string
  invitedByUserId: string | null
  status: string
}

export async function register(account: string, password: string, inviteCode?: string): Promise<AuthTokens> {
  const tokens = await api<AuthTokens>('/api/auth/register', {
    body: { account, password, inviteCode },
  })
  saveTokens(tokens.accessToken, tokens.refreshToken)
  return tokens
}

export async function login(account: string, password: string): Promise<AuthTokens> {
  const tokens = await api<AuthTokens>('/api/auth/login', {
    body: { account, password },
  })
  saveTokens(tokens.accessToken, tokens.refreshToken)
  return tokens
}

export async function logout(): Promise<void> {
  const refreshToken = localStorage.getItem('doggo.refreshToken')
  if (refreshToken) {
    await api<void>('/api/auth/logout', { body: { refreshToken }, auth: false })
  }
  clearTokens()
}

export function fetchProfile(): Promise<Profile> {
  return api<Profile>('/api/auth/me')
}

export function steamLoginUrl(): string {
  return `${import.meta.env.VITE_API_BASE ?? ''}/api/auth/steam/login`
}
