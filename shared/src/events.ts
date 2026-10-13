/**
 * Client event queue for the funnel analytics channel (docs/design/analytics-funnel-design.md §5).
 *
 * trackEvent() only writes to the local queue — it must never throw or await. flushEvents()
 * posts the batch to POST /api/events (anonymous-capable) and puts the whole batch back on
 * failure; the server's idempotency keys make replays free. The same contract is mirrored
 * in web/src/api/events.ts (fetch flavour) — the two ends share the wire format, not code.
 */
import { api, getChannel, newId, getSessionId, kvGet, kvSet } from './client'

// Session and channel helpers live on the shared client (they back the X-Doggo-Session
// request header); re-exported here so callers only need the events module.
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

/**
 * Queue one event. `properties` must already contain only whitelist attributes
 * (productId/source, productId/planId/optionId/quantity, …); this layer drops every
 * other key as a second line of defence behind the server whitelist (design §10.1).
 */
export function trackEvent(
  eventName: string,
  properties?: Record<string, unknown>,
  path?: string
): void {
  try {
    const queue = kvGet<QueuedEvent[]>(QUEUE_KEY) ?? []
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

    kvSet(QUEUE_KEY, queue)

    const due = queue.length >= FLUSH_SIZE || Date.now() - lastFlushAt >= FLUSH_INTERVAL_MS
    if (due) {
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

/** Post the queue. Failures are silent: the batch goes back and the next call retries. */
export async function flushEvents(): Promise<void> {
  if (flushing) return
  const queue = kvGet<QueuedEvent[]>(QUEUE_KEY) ?? []
  if (queue.length === 0) return

  flushing = true
  lastFlushAt = Date.now()
  try {
    const fresh = queue.filter((e) => Date.now() - Date.parse(e.occurredAtUtc) < MAX_AGE_MS)
    if (fresh.length === 0) {
      kvSet(QUEUE_KEY, [])
      return
    }

    // Anonymous-capable call: events from logged-out visitors still reach the funnel.
    // Each event carries its own sessionId, so no session header is needed here; the
    // Authorization header (when logged in) lets the server stamp UserId on the rows.
    await api('/api/events', {
      method: 'POST',
      data: { events: fresh },
      skipAuth: true
    })
    kvSet(QUEUE_KEY, [])
  } catch {
    kvSet(QUEUE_KEY, queue)
  } finally {
    flushing = false
  }
}
