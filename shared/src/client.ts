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

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export function getAccessToken(): string {
  return Taro.getStorageSync<string>(ACCESS_KEY) ?? ''
}

export function getRefreshToken(): string {
  return Taro.getStorageSync<string>(REFRESH_KEY) ?? ''
}

export function saveTokens(accessToken: string, refreshToken: string) {
  Taro.setStorageSync(ACCESS_KEY, accessToken)
  Taro.setStorageSync(REFRESH_KEY, refreshToken)
}

export function clearTokens() {
  Taro.removeStorageSync(ACCESS_KEY)
  Taro.removeStorageSync(REFRESH_KEY)
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  data?: unknown
  skipAuth?: boolean
}

async function rawRequest<T>(path: string, options: RequestOptions): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  const token = getAccessToken()
  if (token && !options.skipAuth) {
    headers.Authorization = `Bearer ${token}`
  }

  const response = await Taro.request({
    url: `${apiBase}${path}`,
    method: options.method ?? 'GET',
    data: options.data as never,
    header: headers
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
