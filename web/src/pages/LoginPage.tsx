import { useContext, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchProfile, login as apiLogin, register as apiRegister, steamLoginUrl } from '../api/auth'
import { ProfileContext } from '../ProfileContext'

type Mode = 'login' | 'register'

/** Account login/registration plus the Steam bind entry. */
export default function LoginPage() {
  const [mode, setMode] = useState<Mode>('login')
  const [account, setAccount] = useState('')
  const [password, setPassword] = useState('')
  const [inviteCode, setInviteCode] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const { setProfile } = useContext(ProfileContext)
  const navigate = useNavigate()

  async function submit() {
    setBusy(true)
    setError('')
    try {
      if (mode === 'login') {
        await apiLogin(account, password)
      } else {
        await apiRegister(account, password, inviteCode || undefined)
      }
      setProfile(await fetchProfile())
      navigate('/')
    } catch (err) {
      setError(String((err as Error).message ?? err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div style={{ maxWidth: 380, margin: '32px auto' }}>
      <div className="card">
        <div style={{ display: 'flex', gap: 12, marginBottom: 16 }}>
          <button className={`btn ${mode === 'login' ? '' : 'secondary'}`} onClick={() => setMode('login')}>
            登录
          </button>
          <button className={`btn ${mode === 'register' ? '' : 'secondary'}`} onClick={() => setMode('register')}>
            注册
          </button>
        </div>
        <div className="field">
          <label>手机号或邮箱</label>
          <input value={account} onChange={(event) => setAccount(event.target.value)} />
        </div>
        <div className="field">
          <label>密码</label>
          <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} />
        </div>
        {mode === 'register' && (
          <div className="field">
            <label>邀请码（可选）</label>
            <input value={inviteCode} onChange={(event) => setInviteCode(event.target.value)} />
          </div>
        )}
        {error && <p className="error-text">{error}</p>}
        <button className="btn" style={{ width: '100%' }} disabled={busy || !account || !password} onClick={submit}>
          {mode === 'login' ? '登录' : '注册'}
        </button>
        <hr style={{ border: 'none', borderTop: '1px solid var(--border)', margin: '16px 0' }} />
        <a
          className="btn secondary"
          style={{ width: '100%', textAlign: 'center', display: 'block' }}
          href={steamLoginUrl()}
        >
          使用 Steam 登录绑定
        </a>
      </div>
    </div>
  )
}
