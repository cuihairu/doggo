import Taro from '@tarojs/taro'

/**
 * Weak-network degradation: list pages keep their last successful payload in
 * storage and render it (with a hint) when a refresh fails.
 */
export function loadCache<T>(key: string): T | null {
  try {
    return (Taro.getStorageSync(key) as T) ?? null
  } catch {
    return null
  }
}

export function saveCache<T>(key: string, value: T) {
  try {
    Taro.setStorageSync(key, value)
  } catch {
    // storage full or unavailable — degradation simply won't kick in
  }
}
