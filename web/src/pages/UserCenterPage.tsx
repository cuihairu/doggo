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

interface CoinLog {
  id: string
  type: number
  delta: number
  balanceAfter: number
  note?: string | null
  createdAtUtc: string
}

interface TradeUrlView {
  id: string
  url: string
  status: number
}

interface RedeemResult {
  type: number
  coinAmount: number
  couponId?: string | null
  couponTitle: string
  balance: number
}

interface ClaimableCoupon {
  id: string
  title: string
  type: number
  amountOffCents: number
  percentOff: number
  minOrderCents: number
}

const COUPON_STATUS: Record<number, string> = { 0: '未使用', 1: '已使用', 2: '已过期' }

const COIN_LOG_TYPE: Record<number, string> = { 0: '发放', 1: '消费', 2: '退款', 3: '过期' }

/** User center: profile, trade link, coin balance, coupons, and this week's sign board. */
export default function UserCenterPage() {
  const { setProfile } = useContext(ProfileContext)
  const [profile, setLocalProfile] = useState<Profile | null>(null)
  const [coins, setCoins] = useState<CoinBalance | null>(null)
  const [coupons, setCoupons] = useState<CouponView[]>([])
  const [sign, setSign] = useState<SignCycle | null>(null)
  const [coinLogs, setCoinLogs] = useState<CoinLog[]>([])
  const [claimable, setClaimable] = useState<ClaimableCoupon[]>([])
  const [tradeUrl, setTradeUrl] = useState<TradeUrlView | null>(null)
  const [tradeInput, setTradeInput] = useState('')
  const [redeemInput, setRedeemInput] = useState('')
  const [redeemMsg, setRedeemMsg] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    fetchProfile()
      .then((loaded) => {
        setLocalProfile(loaded)
        setProfile(loaded)
      })
      .catch((err) => setError(String(err.message ?? err)))
    api<CoinBalance>('/api/coins/balance').then(setCoins).catch(() => {})
    api<{ items: CoinLog[] }>('/api/coins/logs?page=1&pageSize=10')
      .then((result) => setCoinLogs(result.items))
      .catch(() => {})
    api<{ items: CouponView[] }>('/api/coupons/mine')
      .then((result) => setCoupons(result.items))
      .catch(() => {})
    api<ClaimableCoupon[]>('/api/coupons').then(setClaimable).catch(() => {})
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

  async function redeemCode() {
    setRedeemMsg('')
    try {
      const result = await api<RedeemResult>('/api/redeem', { body: { code: redeemInput.trim() } })
      setRedeemInput('')
      setCoins({ balance: result.balance })
      setRedeemMsg(
        result.type === 0
          ? `兑换成功，积分 +${result.coinAmount}`
          : `兑换成功，获得优惠券「${result.couponTitle}」`,
      )
      api<{ items: CouponView[] }>('/api/coupons/mine')
        .then((res) => setCoupons(res.items))
        .catch(() => {})
    } catch (err) {
      setRedeemMsg(String((err as Error).message ?? err))
    }
  }

  async function claim(id: string) {
    setError('')
    try {
      await api(`/api/coupons/${id}/claim`, { body: {} })
      api<{ items: CouponView[] }>('/api/coupons/mine')
        .then((res) => setCoupons(res.items))
        .catch(() => {})
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

        <div className="card">
          <strong>兑换码</strong>
          {redeemMsg && <p className="muted">{redeemMsg}</p>}
          <div className="field">
            <input
              placeholder="输入兑换码"
              value={redeemInput}
              onChange={(event) => setRedeemInput(event.target.value)}
            />
          </div>
          <button className="btn secondary" disabled={!redeemInput.trim()} onClick={redeemCode}>
            兑换
          </button>
        </div>
      </div>

      <h2 className="page-title" style={{ fontSize: 17, marginTop: 24 }}>金币明细</h2>
      <div className="card">
        {coinLogs.length === 0 ? (
          <p className="muted">暂无金币记录</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>时间</th>
                <th>类型</th>
                <th>变动</th>
                <th>余额</th>
                <th>备注</th>
              </tr>
            </thead>
            <tbody>
              {coinLogs.map((log) => (
                <tr key={log.id}>
                  <td className="muted">{log.createdAtUtc.slice(0, 19).replace('T', ' ')}</td>
                  <td>{COIN_LOG_TYPE[log.type] ?? log.type}</td>
                  <td className={log.delta >= 0 ? 'price-up' : 'price-down'}>
                    {log.delta >= 0 ? `+${log.delta}` : log.delta}
                  </td>
                  <td>{log.balanceAfter}</td>
                  <td className="muted">{log.note ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <h2 className="page-title" style={{ fontSize: 17, marginTop: 24 }}>领券中心</h2>
      <div className="card">
        {claimable.length === 0 ? (
          <p className="muted">暂无可领优惠券</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>名称</th>
                <th>面额</th>
                <th>门槛</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {claimable.map((coupon) => (
                <tr key={coupon.id}>
                  <td>{coupon.title}</td>
                  <td>{coupon.type === 0 ? `¥${(coupon.amountOffCents / 100).toFixed(2)}` : `${coupon.percentOff}%`}</td>
                  <td>满 ¥{(coupon.minOrderCents / 100).toFixed(2)}</td>
                  <td>
                    <button className="btn secondary" onClick={() => claim(coupon.id)}>领取</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
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
