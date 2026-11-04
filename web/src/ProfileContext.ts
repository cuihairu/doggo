import { createContext } from 'react'
import type { Profile } from './api/auth'

export interface ProfileContextValue {
  profile: Profile | null
  profileStatus: 'loading' | 'ready' | 'anonymous' | 'unknown'
  setProfile: (profile: Profile | null) => void
  setProfileStatus: (status: 'loading' | 'ready' | 'anonymous') => void
}

export const ProfileContext = createContext<ProfileContextValue>({
  profile: null,
  profileStatus: 'anonymous',
  setProfile: () => {},
  setProfileStatus: () => {},
})
