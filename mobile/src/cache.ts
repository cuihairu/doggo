import { kvGet, kvSet } from '@doggo/shared'

/**
 * Weak-network degradation: list pages keep their last successful payload in
 * storage and render it (with a hint) when a refresh fails.
 */
export function loadCache<T>(key: string): T | null {
  return kvGet<T>(key)
}

export function saveCache<T>(key: string, value: T) {
  kvSet(key, value)
}
