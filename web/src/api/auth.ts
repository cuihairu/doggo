import { api, clearTokens } from './client'

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
  isAdmin: boolean
}

export async function register(account: string, password: string, inviteCode?: string): Promise<AuthTokens> {
  const tokens = await api<AuthTokens>('/api/auth/register', {
    body: { account, password, inviteCode },
  })
  return tokens
}

export async function login(account: string, password: string): Promise<AuthTokens> {
  const tokens = await api<AuthTokens>('/api/auth/login', {
    body: { account, password },
  })
  return tokens
}

export async function logout(): Promise<void> {
  // The server revokes the refresh token held in its httpOnly cookie.
  await api<void>('/api/auth/logout', { body: {}, auth: false })
  clearTokens()
}

export function fetchProfile(): Promise<Profile> {
  return api<Profile>('/api/auth/me')
}

export function steamLoginUrl(): string {
  return `${import.meta.env.VITE_API_BASE ?? ''}/api/auth/steam/login`
}
