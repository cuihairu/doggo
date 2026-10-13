import { PropsWithChildren } from 'react'
import { setClient } from '@doggo/shared'
import { initTheme } from './theme'
import './app.scss'

// Restore the persisted theme (CSS variables resolve per page root; native
// chrome — page background and navigation bar — is set here at startup).
initTheme()

// Funnel design §9.3: server-side visit logs and events split per client.
setClient('miniapp')

export default function App({ children }: PropsWithChildren) {
  return children
}
