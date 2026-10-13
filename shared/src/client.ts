import Taro from '@tarojs/taro'

/**
 * API base: local dev points the devtools at the API with "不校验合法域名" checked;
 * production overrides at build time via TARO_APP_API_BASE. RN calls setApiBase at
 * startup because metro has no define-plugin equivalent for TARO_APP_*.
 */
const API_BASE_ENV = process.env.TARO_APP_API_BASE ?? 'https://api.doggo.example'

let apiBase = API_BASE_ENV

export function getApiBase(): string {
  return apiBase
}

export function setApiBase(base: string) {
  apiBase = base.replace(/\/+$/, '')
}

const ACCESS_KEY = 'doggo.accessToken'
const REFRESH_KEY = 'doggo.refreshToken'

/**
 * Storage seam: mini-program runtimes have synchronous Taro storage, but the
 * taro-rn stubs for *StorageSync return Error objects, which would poison the
 * token headers. Platforms whose Taro lacks working sync storage inject an
 * adapter here at startup (mobile RN backs it with a memory map persisted
 * through AsyncStorage).
 */
export interface StorageAdapter {
  get(key: string): unknown
  set(key: string, value: unknown): void
  remove(key: string): void
}

let storage: StorageAdapter = {
  get: (key) => Taro.getStorageSync(key),
  set: (key, value) => Taro.setStorageSync(key, value),
  remove: (key) => Taro.removeStorageSync(key)
}

export function setStorageAdapter(adapter: StorageAdapter) {
  storage = adapter
}

/** Generic key-value reads behind the same platform seam. Some storages
 * return '' for missing keys, which callers can never distinguish from a
 * stored empty string — both surface here as null. */
export function kvGet<T>(key: string): T | null {
  try {
    const v = storage.get(key)
    if (v == null || v === '') return null
    return v as T
  } catch {
    return null
  }
}

export function kvSet<T>(key: string, value: T) {
  try {
    storage.set(key, value)
  } catch {
    // storage full or unavailable — callers just lose the cache
  }
}

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export function getAccessToken(): string {
  return (storage.get(ACCESS_KEY) as string) ?? ''
}

export function getRefreshToken(): string {
  return (storage.get(REFRESH_KEY) as string) ?? ''
}

export function saveTokens(accessToken: string, refreshToken: string) {
  storage.set(ACCESS_KEY, accessToken)
  storage.set(REFRESH_KEY, refreshToken)
}

export function clearTokens() {
  storage.remove(ACCESS_KEY)
  storage.remove(REFRESH_KEY)
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  data?: unknown
  skipAuth?: boolean
}

/**
 * Client identity sent as the X-Doggo-Client header, so server-side rows (visit logs,
 * funnel events) can be split per client (funnel design §6, §9.3). Apps call setClient
 * once at startup; while it is unset nothing is sent, so the server keeps defaulting to
 * web instead of storing a wrong value.
 */
let client: string | null = null

export function setClient(value: string) {
  client = value
}

async function rawRequest<T>(path: string, options: RequestOptions): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    // Server-authoritative funnel events (order_created) read the client session from here.
    'X-Doggo-Session': getSessionId()
  }
  if (client) {
    headers['X-Doggo-Client'] = client
  }
  const token = getAccessToken()
  if (token && !options.skipAuth) {
    headers.Authorization = `Bearer ${token}`
  }

  const response = await Taro.request({
    url: `${apiBase}${path}`,
    method: options.method ?? 'GET',
    data: options.data as never,
    header: headers,
    // Weak-network guard: fail fast instead of hanging forever.
    timeout: 15000
  })

  if (response.statusCode >= 200 && response.statusCode < 300) {
    return response.data as T
  }

  const problem = response.data as { detail?: string; title?: string } | null
  throw new ApiError(response.statusCode, problem?.detail ?? problem?.title ?? `请求失败 (${response.statusCode})`)
}

/** Request with a transparent access-token refresh on 401, then one retry. */
export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  try {
    return await rawRequest<T>(path, options)
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401 || options.skipAuth) {
      throw error
    }
  }

  const refreshToken = getRefreshToken()
  if (!refreshToken) {
    clearTokens()
    throw new ApiError(401, '请先登录')
  }

  const refreshed = await rawRequest<{ accessToken: string; refreshToken: string }>('/api/auth/refresh', {
    method: 'POST',
    data: { refreshToken },
    skipAuth: true
  }).catch(() => null)

  if (!refreshed) {
    clearTokens()
    throw new ApiError(401, '登录已过期，请重新登录')
  }

  saveTokens(refreshed.accessToken, refreshed.refreshToken)
  return rawRequest<T>(path, options)
}

export const yuan = (cents: number | string | undefined) => (Number(cents ?? 0) / 100).toFixed(2)

// ---- analytics session + channel (used by events.ts and the session header) ----

const SESSION_KEY = 'doggo.analyticsSession'
const CHANNEL_KEY = 'doggo.analyticsChannel'

/** Inactivity after which a new session id is minted (funnel design §4.3). */
const SESSION_IDLE_MS = 30 * 60_000

/** UUID fallback for runtimes without crypto.randomUUID (older RN engines). */
export function newId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }

  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (ch) => {
    const rand = (Math.random() * 16) | 0
    const value = ch === 'x' ? rand : (rand & 0x3) | 0x8
    return value.toString(16)
  })
}

/** Session id survives restarts; 30 minutes without activity starts a new one. */
export function getSessionId(): string {
  const now = Date.now()
  const stored = kvGet<{ id: string; lastActiveAt: number }>(SESSION_KEY)
  if (stored && now - stored.lastActiveAt < SESSION_IDLE_MS) {
    kvSet(SESSION_KEY, { ...stored, lastActiveAt: now })
    return stored.id
  }

  const fresh = { id: newId(), lastActiveAt: now }
  kvSet(SESSION_KEY, fresh)
  return fresh.id
}

/** Campaign/attribution channel set once at startup; defaults to organic server-side too. */
export function setChannel(channel: string) {
  kvSet(CHANNEL_KEY, channel)
}

export function getChannel(): string | null {
  return kvGet<string>(CHANNEL_KEY)
}
