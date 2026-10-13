/**
 * Web flavour of the funnel analytics queue (docs/design/analytics-funnel-design.md §5),
 * mirroring shared/src/events.ts contract-for-contract: same field names, same queue
 * semantics, same idempotency key shape, same never-throw guarantee. localStorage replaces
 * Taro storage; fetch replaces Taro.request. Session and channel helpers live on the
 * client (they back the X-Doggo-Session request header) and are re-exported here.
 */
import { api, getChannel, getSessionId } from './client'

export { getSessionId, setChannel } from './client'

interface QueuedEvent {
  eventName: string
  occurredAtUtc: string
  sessionId: string
  channel: string | null
  schemaVersion: number
  idempotencyKey: string
  path: string | null
  properties: Record<string, unknown> | null
}

const QUEUE_KEY = 'doggo.analyticsQueue'

/** Batch when this many events are queued… */
const FLUSH_SIZE = 10
/** …or this long after the previous flush attempt. */
const FLUSH_INTERVAL_MS = 15_000
/** Local queue cap; the oldest events drop first (stats tolerate loss, memory does not). */
const QUEUE_LIMIT = 500
/** The server rejects events older than this, so stop carrying them. */
const MAX_AGE_MS = 7 * 24 * 60 * 60_000

let flushing = false
let lastFlushAt = 0

function nowIso(): string {
  return new Date().toISOString()
}

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

/**
 * Queue one event. `properties` must already contain only whitelist attributes; this
 * layer drops every other key as a second line of defence behind the server whitelist.
 */
export function trackEvent(
  eventName: string,
  properties?: Record<string, unknown>,
  path?: string
): void {
  try {
    const queue = readJson<QueuedEvent[]>(QUEUE_KEY) ?? []
    queue.push({
      eventName,
      occurredAtUtc: nowIso(),
      sessionId: getSessionId(),
      channel: getChannel(),
      schemaVersion: 1,
      idempotencyKey: `${getSessionId()}:${nowIso()}:${newId()}`,
      path: path ?? null,
      properties: filterKeys(properties)
    })

    while (queue.length > QUEUE_LIMIT) {
      queue.shift()
    }

    localStorage.setItem(QUEUE_KEY, JSON.stringify(queue))

    if (queue.length >= FLUSH_SIZE || Date.now() - lastFlushAt >= FLUSH_INTERVAL_MS) {
      void flushEvents()
    }
  } catch {
    // Analytics must never break the page.
  }
}

function filterKeys(properties?: Record<string, unknown>): Record<string, unknown> | null {
  if (!properties) return null
  const filtered: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(properties)) {
    if (value !== undefined) filtered[key] = value
  }
  return Object.keys(filtered).length > 0 ? filtered : null
}

function readJson<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as T) : null
  } catch {
    return null
  }
}

/** Post the queue. Failures are silent: the batch goes back and the next call retries. */
export async function flushEvents(): Promise<void> {
  if (flushing) return
  const queue = readJson<QueuedEvent[]>(QUEUE_KEY) ?? []
  if (queue.length === 0) return

  flushing = true
  lastFlushAt = Date.now()
  try {
    const fresh = queue.filter((e) => Date.now() - Date.parse(e.occurredAtUtc) < MAX_AGE_MS)
    if (fresh.length === 0) {
      localStorage.setItem(QUEUE_KEY, JSON.stringify([]))
      return
    }

    // The bearer token rides along for logged-in visitors so the server stamps UserId.
    await api<{ accepted: number; duplicates: number; rejected: number }>('/api/events', {
      method: 'POST',
      body: { events: fresh },
      auth: false
    })
    localStorage.setItem(QUEUE_KEY, JSON.stringify([]))
  } catch {
    localStorage.setItem(QUEUE_KEY, JSON.stringify(queue))
  } finally {
    flushing = false
  }
}
