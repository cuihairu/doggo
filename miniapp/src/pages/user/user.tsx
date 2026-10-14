import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Button, Input } from '@tarojs/components'
import Taro from '@tarojs/taro'
import {
  fetchCoinBalance, fetchCoinLogs, fetchMyCoupons, fetchProfile, fetchSignCycle,
  fetchTradeUrls, addTradeUrl, fetchClaimableCoupons, claimCoupon, fetchInviteRewards,
  logout, redeemCode, Profile, signToday, TradeUrlEntry, CoinLogEntry, ClaimableCoupon, InviteRewardView
} from '../../api/endpoints'
import { ApiError, getAccessToken } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'
import { THEMES, THEME_SWATCH, useTheme, setTheme } from '../../theme'

interface CouponLite {
  id: string
  title: string
  amountOffCents: number
  percentOff: number
  status: number
  validToUtc?: string | null
}

const COIN_LOG_TYPE: Record<number, string> = { 0: '发放', 1: '消费', 2: '退款', 3: '过期' }

/** Keeps the middle of a phone number or email, as the profile head shows. */
function maskAccount(account: string): string {
  if (/^\d+$/.test(account) && account.length > 7) {
    return `${account.slice(0, 3)}****${account.slice(-4)}`
  }
  const at = account.indexOf('@')
  if (at > 1) {
    return `${account.slice(0, 1)}***${account.slice(at)}`
  }
  return account
}

/** User center: identity, coins, coupons, weekly sign-in. */
export default function UserPage() {
  const theme = useTheme()
  const [profile, setProfile] = useState<Profile | null>(null)
  const [coins, setCoins] = useState<number | null>(null)
  const [coupons, setCoupons] = useState<CouponLite[]>([])
  const [signed, setSigned] = useState<boolean | null>(null)
  const [signCycle, setSignCycle] = useState<{ signedDays: number; totalDays: number; todayReward: number } | null>(null)
  const [redeemInput, setRedeemInput] = useState('')
  const [tradeUrls, setTradeUrls] = useState<TradeUrlEntry[]>([])
  const [tradeInput, setTradeInput] = useState('')
  const [coinLogs, setCoinLogs] = useState<CoinLogEntry[]>([])
  const [claimable, setClaimable] = useState<ClaimableCoupon[]>([])
  const [invites, setInvites] = useState<{ total: number; totalReward: number; items: InviteRewardView[] } | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    if (!getAccessToken()) {
      setError('未登录')
      return
    }
    fetchProfile().then((p) => {
      setProfile(p)
      setError('')
      fetchTradeUrls(p.id).then(setTradeUrls).catch(() => {})
    }).catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
    fetchCoinBalance().then((b) => setCoins(b.balance)).catch(() => {})
    fetchCoinLogs(1, 5).then((page) => setCoinLogs(page.items)).catch(() => {})
    // Unused = 0 / Used = 1 / Expired = 2 — "available" means unused and unexpired.
    fetchMyCoupons()
      .then((rows) => setCoupons(rows.filter((c) => c.status === 0 && (!c.validToUtc || new Date(c.validToUtc) > new Date()))))
      .catch(() => {})
    fetchSignCycle().then((cycle) => {
      setSigned(cycle.todaySigned)
      const today = new Date().toISOString().slice(0, 10)
      setSignCycle({
        signedDays: cycle.days.filter((d) => d.signed).length,
        totalDays: cycle.days.length || 7,
        todayReward: cycle.days.find((d) => d.date === today)?.coinReward ?? 0,
      })
    }).catch(() => {})
    fetchClaimableCoupons().then(setClaimable).catch(() => {})
    fetchInviteRewards(1, 10).then(setInvites).catch(() => {})
  }, [])

  useEffect(() => {
    // Tab pages re-enter without a remount in some flows; refresh on show.
    load()
  }, [load])

  async function doSign() {
    try {
      const result = await signToday()
      setSigned(true)
      Taro.showToast({ title: `+${result.coinReward} 金币`, icon: 'success' })
      fetchCoinBalance().then((b) => setCoins(b.balance)).catch(() => {})
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '签到失败', icon: 'none' })
    }
  }

  async function doRedeem() {
    const code = redeemInput.trim()
    if (!code) return
    try {
      const result = await redeemCode(code)
      setRedeemInput('')
      setCoins(result.balance)
      Taro.showToast({
        title: result.type === 0 ? `兑换成功，金币 +${result.coinAmount}` : `获得优惠券「${result.couponTitle}」`,
        icon: 'success'
      })
      load()
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '兑换失败', icon: 'none' })
    }
  }

  async function doAddTradeUrl() {
    const url = tradeInput.trim()
    if (!url || !profile) return
    try {
      await addTradeUrl(profile.id, url)
      setTradeInput('')
      setTradeUrls(await fetchTradeUrls(profile.id))
      Taro.showToast({ title: '已保存', icon: 'success' })
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '保存失败', icon: 'none' })
    }
  }

  async function doClaim(couponId: string) {
    try {
      const claimed = await claimCoupon(couponId)
      Taro.showToast({ title: `已领取「${claimed.title}」`, icon: 'success' })
      load()
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '领取失败', icon: 'none' })
    }
  }

  if (error === '未登录') {
    return (
      <View className="card">
        <Text className="muted">登录后查看资料、金币、优惠券与签到</Text>
        <View className="btn-primary" style={{ marginTop: '16rpx' }} onClick={() => Taro.navigateTo({ url: '/pages/login/login' })}>
          去登录
        </View>
      </View>
    )
  }

  return (
    <ThemeRoot>
      <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      <View className="card me-head">
        <View className="avatar"><Text className="avatar-text">{(profile?.nickname ?? '?').slice(0, 1)}</Text></View>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: '32rpx', fontWeight: 700 }}>{profile?.nickname ?? '加载中…'}</Text>
          {profile && (
            <Text className="muted">
              {maskAccount(profile.phone || profile.email || '微信用户')} · 邀请码 {profile.inviteCode}
            </Text>
          )}
        </View>
      </View>

      <View className="stat-row">
        <View className="stat-cell">
          <Text className="stat-value price">{coins ?? '—'}</Text>
          <Text className="muted">金币</Text>
        </View>
        <View className="stat-cell">
          <Text className="stat-value">{coupons.length}</Text>
          <Text className="muted">优惠券</Text>
        </View>
        <View className="stat-cell">
          <Text className="stat-value">{invites?.total ?? 0}</Text>
          <Text className="muted">邀请</Text>
        </View>
      </View>

      <View className="card row" style={{ justifyContent: 'space-between' }}>
        <Text className="muted">
          本周签到 {signCycle?.signedDays ?? 0}/{signCycle?.totalDays ?? 7}
          {signed || !signCycle?.todayReward ? '' : ` · 今天可 +${signCycle.todayReward}`}
        </Text>
        <Button size="mini" className="btn-primary" disabled={signed === true} onClick={doSign}>
          {signed === true ? '今日已签到' : '签到'}
        </Button>
      </View>

      <View className="card">
        {coinLogs.length > 0 && (
          <View style={{ marginTop: '12rpx' }}>
            <Text className="muted">金币明细</Text>
            {coinLogs.map((log) => (
              <View key={log.id} className="row" style={{ justifyContent: 'space-between', marginTop: '8rpx' }}>
                <Text className="muted">{COIN_LOG_TYPE[log.type] ?? log.type} · {log.createdAtUtc.slice(0, 10)}</Text>
                <Text style={{ color: log.delta >= 0 ? 'var(--primary)' : 'var(--danger)', fontWeight: 600 }}>
                  {log.delta >= 0 ? `+${log.delta}` : log.delta}
                </Text>
              </View>
            ))}
          </View>
        )}
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>Steam 交易链接</Text>
        {tradeUrls.map((link) => (
          <View key={link.id} className="row" style={{ justifyContent: 'space-between', marginTop: '10rpx' }}>
            <Text style={{ flex: 1 }} numberOfLines={1}>{link.isPreferred ? '★ ' : ''}{link.url}</Text>
            <Text className="muted">{link.status === 1 ? '已验证' : link.status === 2 ? '无效' : '未验证'}</Text>
          </View>
        ))}
        {tradeUrls.length === 0 && <Text className="muted">尚未绑定，饰品取回前需要</Text>}
        <Input
          placeholder="粘贴 Steam 交易链接"
          value={tradeInput}
          onInput={(e) => setTradeInput(e.detail.value)}
          style={{ border: '1rpx solid var(--border)', borderRadius: '8rpx', padding: '12rpx', margin: '20rpx 0' }}
        />
        <Button size="mini" className="btn-primary" disabled={!tradeInput.trim()} onClick={doAddTradeUrl}>
          保存
        </Button>
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>兑换码</Text>
        <Input
          placeholder="输入兑换码"
          value={redeemInput}
          onInput={(e) => setRedeemInput(e.detail.value)}
          style={{ border: '1rpx solid var(--border)', borderRadius: '8rpx', padding: '12rpx', margin: '20rpx 0' }}
        />
        <Button size="mini" className="btn-primary" disabled={!redeemInput.trim()} onClick={doRedeem}>
          兑换
        </Button>
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>邀请返利</Text>
        <Text className="muted">
          已成功邀请 {invites?.total ?? 0} 人，累计奖励 {invites?.totalReward ?? 0} 金币
        </Text>
        {invites && invites.items.length === 0 ? (
          <Text className="muted">还没有返利记录，把邀请码分享给好友即可</Text>
        ) : (
          invites?.items.map((invite) => (
            <View key={invite.id} className="row" style={{ justifyContent: 'space-between', marginTop: '10rpx' }}>
              <Text>{invite.inviteeNickname || '—'}</Text>
              <Text>+{invite.coinReward} 金币</Text>
            </View>
          ))
        )}
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>领券中心</Text>
        {claimable.map((coupon) => (
          <View key={coupon.id} className="row" style={{ justifyContent: 'space-between', marginTop: '10rpx' }}>
            <View style={{ flex: 1 }}>
              <Text>{coupon.title}</Text>
              <Text className="muted">满 ¥{(coupon.minOrderCents / 100).toFixed(2)} 可用</Text>
            </View>
            <Button size="mini" className="btn-primary" onClick={() => doClaim(coupon.id)}>领取</Button>
          </View>
        ))}
        {claimable.length === 0 && <Text className="muted">暂无可领优惠券</Text>}
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>可用优惠券 ({coupons.length})</Text>
        {coupons.map((coupon) => (
          <View key={coupon.id} className="row" style={{ justifyContent: 'space-between', marginTop: '10rpx' }}>
            <Text>{coupon.title}</Text>
            <Text className="price">
              {coupon.amountOffCents > 0 ? `−¥${(coupon.amountOffCents / 100).toFixed(2)}` : `${coupon.percentOff}% off`}
            </Text>
          </View>
        ))}
        {coupons.length === 0 && <Text className="muted">暂无可用优惠券</Text>}
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>主题外观</Text>
        <Text className="muted">选择后立即生效并记住到本机；布局不变，只换颜色。</Text>
        <View className="theme-grid">
          {THEMES.map((item) => (
            <View
              key={item.id}
              className="theme-opt"
              aria-pressed={theme === item.id}
              onClick={() => setTheme(item.id)}
            >
              <View className="theme-swatch" style={{ background: THEME_SWATCH[item.id] }} />
              <Text>{item.label}</Text>
            </View>
          ))}
        </View>
      </View>

      <View className="card">
        <View className="row" style={{ justifyContent: 'space-between' }}>
          <View className="btn-primary" style={{ flex: 1, marginRight: '8rpx' }} onClick={() => Taro.navigateTo({ url: '/pages/opening/opening' })}>
            我的奖品
          </View>
          <View className="btn-primary" style={{ flex: 1, marginLeft: '8rpx' }} onClick={() => { logout(); setProfile(null); setError('未登录') }}>
            退出登录
          </View>
        </View>
      </View>
      </ScrollView>
    </ThemeRoot>
  )
}
