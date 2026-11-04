const API_BASE = import.meta.env.VITE_API_BASE ?? ''

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

interface RequestOptions {
  method?: string
  body?: unknown
  auth?: boolean
}

/**
 * Thin JSON client for the Doggo API: bearer token injection, ProblemDetails error
 * extraction, and one transparent refresh-token retry when an access token expires.
 */
export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await request(path, options)
  if (response.status === 401 && options.auth !== false) {
    const refreshStatus = await tryRefresh()
    if (refreshStatus === 200) {
      return api<T>(path, { ...options, auth: false })
    }
    if (refreshStatus >= 500) {
      throw new ApiError(refreshStatus, `Authentication refresh failed (${refreshStatus})`)
    }
  }
  return parseBody<T>(response)
}

async function request(path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = {
    // Server-authoritative funnel events (order_created) read the client session from here.
    'X-Doggo-Session': getSessionId(),
    // Funnel design §9.3: server-side visit logs and events split per client.
    'X-Doggo-Client': 'web'
  }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  return fetch(`${API_BASE}${path}`, {
    method: options.method ?? (options.body !== undefined ? 'POST' : 'GET'),
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    credentials: 'same-origin',
  })
}

async function tryRefresh(): Promise<number> {
  const response = await fetch(`${API_BASE}/api/auth/refresh`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      // Server decides the browser (cookie) path from this header.
      'X-Doggo-Client': 'web',
    },
    credentials: 'same-origin',
  })
  return response.status
}

async function parseBody<T>(response: Response): Promise<T> {
  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  const payload = text ? JSON.parse(text) : {}
  if (!response.ok) {
    // ASP.NET ProblemDetails carries the message in `detail` or `title`.
    const message = payload.detail ?? payload.title ?? `Request failed (${response.status})`
    throw new ApiError(response.status, message)
  }
  return payload as T
}

// ---- analytics session + channel (used by events.ts and the session header) ----

const SESSION_KEY = 'doggo.analyticsSession'
const CHANNEL_KEY = 'doggo.analyticsChannel'

/** Inactivity after which a new session id is minted (funnel design §4.3). */
const SESSION_IDLE_MS = 30 * 60_000

function newId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }

  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (ch) => {
    const rand = (Math.random() * 16) | 0
    const value = ch === 'x' ? rand : (rand & 0x3) | 0x8
    return value.toString(16)
  })
}

/** Session id survives reloads; 30 minutes without activity starts a new one. */
export function getSessionId(): string {
  const now = Date.now()
  try {
    const raw = localStorage.getItem(SESSION_KEY)
    const stored = raw ? (JSON.parse(raw) as { id: string; lastActiveAt: number }) : null
    if (stored && now - stored.lastActiveAt < SESSION_IDLE_MS) {
      localStorage.setItem(SESSION_KEY, JSON.stringify({ ...stored, lastActiveAt: now }))
      return stored.id
    }
  } catch {
    // fall through to a fresh session
  }

  const fresh = { id: newId(), lastActiveAt: now }
  localStorage.setItem(SESSION_KEY, JSON.stringify(fresh))
  return fresh.id
}

/** Campaign/attribution channel set once at startup; defaults to organic server-side too. */
export function setChannel(channel: string) {
  localStorage.setItem(CHANNEL_KEY, JSON.stringify(channel))
}

export function getChannel(): string | null {
  try {
    const raw = localStorage.getItem(CHANNEL_KEY)
    return raw ? (JSON.parse(raw) as string) : null
  } catch {
    return null
  }
}
