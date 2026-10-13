import { PropsWithChildren } from 'react'
import { setApiBase, setClient } from '@doggo/shared'
import { installStorage, preloadStorage } from './storage'
import { hydrateTheme } from './theme'

// RN has no working Taro sync storage; route the shared layer through the
// AsyncStorage-backed adapter and preload persisted values.
installStorage()
preloadStorage()

// Restore the persisted theme before the first frame when possible; a late
// resolve simply re-renders once with the stored palette.
hydrateTheme()

// Emulator default: 10.0.2.2 is the host loopback from the Android emulator.
// Override at bundle time with TARO_APP_API_BASE.
setApiBase(process.env.TARO_APP_API_BASE ?? 'http://10.0.2.2:5077')

// Funnel design §9.3: server-side visit logs and events split per client.
setClient('mobile')

// The RN entry generator does `import Component from './app'` and renders it,
// so the app module must default-export a passthrough component.
export default function App({ children }: PropsWithChildren) {
  return children
}
