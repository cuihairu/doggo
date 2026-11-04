import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { fetchProfile, logout, type Profile } from './api/auth'
import { ProfileContext, type ProfileContextValue } from './ProfileContext'
import { useTheme, type ThemeId } from './theme'

export default function App() {
  const [profile, setProfile] = useState<Profile | null>(null)
  const [profileStatus, setProfileStatus] = useState<ProfileContextValue['profileStatus']>('loading')
  const [signOutError, setSignOutError] = useState('')
  const navigate = useNavigate()
  const { theme, themes, setTheme } = useTheme()

  useEffect(() => {
    // The access token lives in an httpOnly cookie, so presence is only known by
    // asking the server; a 401 simply means the visitor is signed out.
    fetchProfile()
      .then((loadedProfile) => {
        setProfile(loadedProfile)
        setProfileStatus('ready')
      })
      .catch(() => {
        setProfile(null)
        setProfileStatus('unknown')
      })
  }, [])

  const signedIn = profileStatus === 'ready' && profile !== null

  async function handleSignOut() {
    try {
      await logout()
      setProfile(null)
      setProfileStatus('anonymous')
      setSignOutError('')
      navigate('/')
    } catch {
      setSignOutError('退出失败，请重试')
    }
  }

  return (
    <ProfileContext.Provider value={{ profile, profileStatus, setProfile, setProfileStatus }}>
      <div className="app-shell">
        <nav className="app-nav">
          <span className="app-logo">Doggo</span>
          <NavLink to="/" end>资讯</NavLink>
          <NavLink to="/market">行情</NavLink>
          <NavLink to="/products">商店</NavLink>
          <NavLink to="/roll">Roll 房</NavLink>
          <span className="spacer" />
          <select
            className="theme-switch"
            aria-label="主题"
            value={theme}
            onChange={(event) => setTheme(event.target.value as ThemeId)}
          >
            {themes.map((item) => (
              <option key={item.id} value={item.id}>
                {item.label}
              </option>
            ))}
          </select>
          {profileStatus === 'loading' ? (
            <span className="muted" aria-busy="true">检查登录…</span>
          ) : signedIn ? (
            <>
              <span className="app-user">{profile.nickname}</span>
              <NavLink to="/user">用户中心</NavLink>
              <a
                href="#signout"
                onClick={(event) => {
                  event.preventDefault()
                  void handleSignOut()
                }}
              >
                退出
              </a>
            </>
          ) : (
            <NavLink to="/login">登录</NavLink>
          )}
          {signOutError && <span className="error-text">{signOutError}</span>}
        </nav>
        <main className="app-main">
          <Outlet />
        </main>
      </div>
    </ProfileContext.Provider>
  )
}
