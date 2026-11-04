import { createContext } from 'react'
import type { Profile } from './api/auth'

const PROFILE_STATUSES = ['loading', 'ready', 'anonymous', 'unknown'] as const

export type ProfileStatus = (typeof PROFILE_STATUSES)[number]

export interface ProfileContextValue {
  profile: Profile | null
  profileStatus: ProfileStatus
  setProfile: (profile: Profile | null) => void
  setProfileStatus: (status: ProfileStatus) => void
}

export const ProfileContext = createContext<ProfileContextValue>({
  profile: null,
  profileStatus: 'anonymous',
  setProfile: () => {},
  setProfileStatus: () => {},
})
