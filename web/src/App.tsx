import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { clearTokens, getAccessToken } from './api/client'
import { fetchProfile, type Profile } from './api/auth'
import { ProfileContext } from './ProfileContext'
import { useTheme, type ThemeId } from './theme'

export default function App() {
  const [profile, setProfile] = useState<Profile | null>(null)
  const navigate = useNavigate()
  const { theme, themes, setTheme } = useTheme()

  useEffect(() => {
    if (!getAccessToken()) {
      return
    }
    fetchProfile()
      .then(setProfile)
      .catch(() => {
        clearTokens()
        setProfile(null)
      })
  }, [])

  const signedIn = profile !== null

  function handleSignOut() {
    clearTokens()
    setProfile(null)
    navigate('/')
  }

  return (
    <ProfileContext.Provider value={{ profile, setProfile }}>
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
          {signedIn ? (
            <>
              <span className="app-user">{profile.nickname}</span>
              <NavLink to="/user">用户中心</NavLink>
              <a
                href="#signout"
                onClick={(event) => {
                  event.preventDefault()
                  handleSignOut()
                }}
              >
                退出
              </a>
            </>
          ) : (
            <NavLink to="/login">登录</NavLink>
          )}
        </nav>
        <main className="app-main">
          <Outlet />
        </main>
      </div>
    </ProfileContext.Provider>
  )
}
