import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Button, Input } from '@tarojs/components'
import Taro from '@tarojs/taro'
import {
  fetchCoinBalance, fetchMyCoupons, fetchProfile, fetchSignCycle,
  logout, redeemCode, Profile, signToday
} from '../../api/endpoints'
import { ApiError, getAccessToken } from '../../api/client'

interface CouponLite {
  id: string
  title: string
  amountOffCents: number
  percentOff: number
  status: number
  validToUtc?: string | null
}

/** User center: identity, coins, coupons, weekly sign-in. */
export default function UserPage() {
  const [profile, setProfile] = useState<Profile | null>(null)
  const [coins, setCoins] = useState<number | null>(null)
  const [coupons, setCoupons] = useState<CouponLite[]>([])
  const [signed, setSigned] = useState<boolean | null>(null)
  const [redeemInput, setRedeemInput] = useState('')
  const [error, setError] = useState('')

  const load = useCallback(() => {
    if (!getAccessToken()) {
      setError('未登录')
      return
    }
    fetchProfile().then((p) => {
      setProfile(p)
      setError('')
    }).catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
    fetchCoinBalance().then((b) => setCoins(b.balance)).catch(() => {})
    // Unused = 0 / Used = 1 / Expired = 2 — "available" means unused and unexpired.
    fetchMyCoupons()
      .then((rows) => setCoupons(rows.filter((c) => c.status === 0 && (!c.validToUtc || new Date(c.validToUtc) > new Date()))))
      .catch(() => {})
    fetchSignCycle().then((cycle) => setSigned(cycle.todaySigned)).catch(() => {})
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
    <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      <View className="card">
        <Text style={{ fontSize: '32rpx', fontWeight: 700 }}>{profile?.nickname ?? '加载中…'}</Text>
        {profile && <Text className="muted">邀请码 {profile.inviteCode}</Text>}
        {profile && <Text className="muted">{profile.phone || profile.email || '微信用户'}</Text>}
      </View>

      <View className="card row" style={{ justifyContent: 'space-between' }}>
        <View>
          <Text className="muted">金币余额</Text>
          <Text style={{ fontSize: '36rpx', fontWeight: 700 }}>{coins ?? '—'}</Text>
        </View>
        <Button size="mini" className="btn-primary" disabled={signed === true} onClick={doSign}>
          {signed === true ? '今日已签到' : '每日签到'}
        </Button>
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>兑换码</Text>
        <Input
          placeholder="输入兑换码"
          value={redeemInput}
          onInput={(e) => setRedeemInput(e.detail.value)}
          style={{ border: '1rpx solid #eee', borderRadius: '8rpx', padding: '12rpx', margin: '20rpx 0' }}
        />
        <Button size="mini" className="btn-primary" disabled={!redeemInput.trim()} onClick={doRedeem}>
          兑换
        </Button>
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
  )
}
