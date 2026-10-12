import AsyncStorage from '@react-native-async-storage/async-storage'
import { setStorageAdapter, type StorageAdapter } from '@doggo/shared'

// The shared layer reads tokens synchronously, but taro-rn's *StorageSync
// stubs return Error objects. This adapter backs the reads with a memory map
// and persists every write through AsyncStorage (write-through, preloaded at
// startup), so no Taro storage API is ever called on RN.
const mem = new Map<string, string>()

export const rnStorageAdapter: StorageAdapter = {
  get(key) {
    const raw = mem.get(key)
    if (raw === undefined) return null
    try {
      return JSON.parse(raw) as unknown
    } catch {
      return raw
    }
  },
  set(key, value) {
    const raw = JSON.stringify(value)
    mem.set(key, raw)
    AsyncStorage.setItem(key, raw).catch(() => {})
  },
  remove(key) {
    mem.delete(key)
    AsyncStorage.removeItem(key).catch(() => {})
  }
}

export function installStorage() {
  setStorageAdapter(rnStorageAdapter)
}

// Warm the memory map so a restart keeps tokens and caches. Requests fired
// before this resolves simply go unauthenticated, which public endpoints accept.
export function preloadStorage() {
  AsyncStorage.getAllKeys()
    .then((keys) => (keys.length ? AsyncStorage.multiGet(keys) : Promise.resolve([])))
    .then((pairs) => {
      for (const [k, v] of pairs) if (v != null) mem.set(k, v)
    })
    .catch(() => {})
}
