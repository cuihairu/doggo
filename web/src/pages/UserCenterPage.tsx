import { useCallback, useContext, useEffect, useState } from 'react'
import { api } from '../api/client'
import { fetchProfile, type Profile } from '../api/auth'
import { ProfileContext } from '../ProfileContext'

interface CoinBalance {
  balance: number
}

interface CouponView {
  id: string
  title: string
  type: number
  amountOffCents: number
  percentOff: number
  minOrderCents: number
  status: number
  validToUtc?: string | null
}

interface SignCycle {
  cycleId: string
  days: { date: string; signed: boolean; coinReward: number }[]
  todaySigned: boolean
  totalReward: number
}

interface TradeUrlView {
  id: string
  url: string
  status: number
}

const COUPON_STATUS: Record<number, string> = { 0: '未使用', 1: '已使用', 2: '已过期' }

/** User center: profile, trade link, coin balance, coupons, and this week's sign board. */
export default function UserCenterPage() {
  const { setProfile } = useContext(ProfileContext)
  const [profile, setLocalProfile] = useState<Profile | null>(null)
  const [coins, setCoins] = useState<CoinBalance | null>(null)
  const [coupons, setCoupons] = useState<CouponView[]>([])
  const [sign, setSign] = useState<SignCycle | null>(null)
  const [tradeUrl, setTradeUrl] = useState<TradeUrlView | null>(null)
  const [tradeInput, setTradeInput] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    fetchProfile()
      .then((loaded) => {
        setLocalProfile(loaded)
        setProfile(loaded)
      })
      .catch((err) => setError(String(err.message ?? err)))
    api<CoinBalance>('/api/coins/balance').then(setCoins).catch(() => {})
    api<{ items: CouponView[] }>('/api/coupons/mine')
      .then((result) => setCoupons(result.items))
      .catch(() => {})
    api<SignCycle>('/api/sign').then(setSign).catch(() => {})
  }, [setProfile])

  const reloadTradeUrl = useCallback(() => {
    if (!profile) return
    api<TradeUrlView[]>(`/api/users/${profile.id}/trade-urls`)
      .then((links) => setTradeUrl(links[0] ?? null))
      .catch(() => {})
  }, [profile])

  useEffect(() => {
    reloadTradeUrl()
  }, [reloadTradeUrl])

  async function signToday() {
    try {
      await api('/api/sign', { body: {} })
      setSign(await api<SignCycle>('/api/sign'))
      setCoins(await api<CoinBalance>('/api/coins/balance'))
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  async function saveTradeUrl() {
    if (!profile) return
    try {
      await api(`/api/users/${profile.id}/trade-urls`, { body: { url: tradeInput, nickname: null } })
      setTradeInput('')
      reloadTradeUrl()
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  if (!profile) return <p className="muted">{error || '请先登录'}</p>

  return (
    <div>
      <h1 className="page-title">用户中心</h1>
      {error && <p className="error-text">{error}</p>}
      <div className="grid cols-3">
        <div className="card">
          <strong>{profile.nickname}</strong>
          <p className="muted">{profile.phone || profile.email}</p>
          <p className="muted">邀请码 {profile.inviteCode}</p>
          <p>
            积分余额 <strong style={{ color: 'var(--primary)' }}>{coins?.balance ?? '…'}</strong>
          </p>
        </div>

        <div className="card">
          <strong>Steam 交易链接</strong>
          {tradeUrl ? (
            <p className="muted" style={{ wordBreak: 'break-all' }}>
              {tradeUrl.url}
              <br />
              状态 {tradeUrl.status === 1 ? '已验证' : '未验证'}
            </p>
          ) : (
            <p className="muted">尚未绑定</p>
          )}
          <div className="field">
            <input
              placeholder="粘贴交易链接"
              value={tradeInput}
              onChange={(event) => setTradeInput(event.target.value)}
            />
          </div>
          <button className="btn secondary" disabled={!tradeInput} onClick={saveTradeUrl}>
            保存
          </button>
        </div>

        <div className="card">
          <strong>本周签到</strong>
          <p className="muted">
            周期 {sign?.cycleId ?? '…'} · 累计 {sign?.totalReward ?? 0} 积分
          </p>
          <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap', margin: '10px 0' }}>
            {sign?.days.map((day) => (
              <span
                key={day.date}
                style={{
                  padding: '4px 8px',
                  borderRadius: 4,
                  fontSize: 12,
                  background: day.signed ? 'var(--primary-weak)' : 'var(--bg)',
                  color: day.signed ? 'var(--primary)' : 'var(--text-muted)',
                }}
              >
                {day.date.slice(5)}
              </span>
            ))}
          </div>
          <button className="btn" disabled={sign?.todaySigned} onClick={signToday}>
            {sign?.todaySigned ? '今日已签' : '签到'}
          </button>
        </div>
      </div>

      <h2 className="page-title" style={{ fontSize: 17, marginTop: 24 }}>我的优惠券</h2>
      <div className="card">
        {coupons.length === 0 ? (
          <p className="muted">暂无优惠券</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>名称</th>
                <th>面额</th>
                <th>门槛</th>
                <th>状态</th>
              </tr>
            </thead>
            <tbody>
              {coupons.map((coupon) => (
                <tr key={coupon.id}>
                  <td>{coupon.title}</td>
                  <td>{coupon.type === 0 ? `¥${(coupon.amountOffCents / 100).toFixed(2)}` : `${coupon.percentOff}%`}</td>
                  <td>满 ¥{(coupon.minOrderCents / 100).toFixed(2)}</td>
                  <td>{COUPON_STATUS[coupon.status] ?? coupon.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
