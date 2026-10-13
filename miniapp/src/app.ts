import { PropsWithChildren } from 'react'
import { setClient } from '@doggo/shared'
import './app.scss'

// Funnel design §9.3: server-side visit logs and events split per client.
setClient('miniapp')

export default function App({ children }: PropsWithChildren) {
  return children
}
