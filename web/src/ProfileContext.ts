import { createContext } from 'react'
import type { Profile } from './api/auth'

export interface ProfileContextValue {
  profile: Profile | null
  setProfile: (profile: Profile | null) => void
}

export const ProfileContext = createContext<ProfileContextValue>({
  profile: null,
  setProfile: () => {},
})
