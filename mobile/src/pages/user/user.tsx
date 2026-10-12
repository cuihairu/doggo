import { useCallback, useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro, { useDidShow } from '@tarojs/taro'
import {
  fetchCoinBalance, fetchMyCoupons, fetchProfile, fetchSignCycle,
  logout, signToday, ApiError, getAccessToken, Profile
} from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
import { useTheme } from '../../theme'

/** User center: identity, coins, coupons, weekly sign-in, entries. */
export default function UserPage() {
  const t = useTheme()
  const [profile, setProfile] = useState<Profile | null>(null)
  const [coins, setCoins] = useState<number | null>(null)
  const [coupons, setCoupons] = useState<{ id: string; title: string; amountOffCents: number; percentOff: number }[]>([])
  const [signed, setSigned] = useState<boolean | null>(null)
  const [error, setError] = useState('')
  const [signBusy, setSignBusy] = useState(false)

  const load = useCallback(() => {
    if (!getAccessToken()) {
      setError('未登录')
      return
    }
    setError('')
    fetchProfile()
      .then((p) => { setProfile(p); setError('') })
      .catch((e) => {
        if (e instanceof ApiError && e.status === 401) {
          setError('未登录')
          return
        }
        setError(e instanceof ApiError ? e.message : '加载失败')
      })
    fetchCoinBalance().then((b) => setCoins(b.balance)).catch(() => {})
    // Unused = 0 / Used = 1 / Expired = 2 — "available" means unused and unexpired.
    fetchMyCoupons()
      .then((rows) => setCoupons(rows.filter((c) => c.status === 0 && (!c.validToUtc || new Date(c.validToUtc) > new Date()))))
      .catch(() => {})
    fetchSignCycle().then((cycle) => setSigned(cycle.todaySigned)).catch(() => {})
  }, [])

  useEffect(() => {
    load()
  }, [load])

  // The page stays mounted across navigateTo round-trips (e.g. claiming a
  // coupon on the product page); refresh so coins and coupons don't go stale.
  useDidShow(() => {
    load()
  })

  async function doSign() {
    setSignBusy(true)
    try {
      const result = await signToday()
      setSigned(true)
      Taro.showToast({ title: `+${result.coinReward} 金币`, icon: 'success' })
      fetchCoinBalance().then((b) => setCoins(b.balance)).catch(() => {})
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '签到失败', icon: 'none' })
    } finally {
      setSignBusy(false)
    }
  }

  if (error === '未登录') {
    return (
      <View style={[styles.page, { backgroundColor: t.bg }]}>
        <SafeTop style={{ backgroundColor: t.bg }} />
        <View style={{ padding: 16 }}>
          <Card>
            <Muted>登录后查看资料、金币、优惠券与签到</Muted>
            <Btn onPress={() => Taro.navigateTo({ url: '/pages/login/login' })} style={{ marginTop: 12 }}>去登录</Btn>
          </Card>
        </View>
      </View>
    )
  }

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        {error ? <Msg text={error} onRetry={load} /> : null}
        <Card>
          <Text style={[styles.name, { color: t.text }]}>{profile?.nickname ?? '加载中…'}</Text>
          {profile ? <Muted>邀请码 {profile.inviteCode}</Muted> : null}
          {profile ? <Muted>{profile.phone || profile.email || '账号用户'}</Muted> : null}
        </Card>

        <Card>
          <View style={styles.row}>
            <View>
              <Muted>金币余额</Muted>
              <Text style={[styles.coins, { color: t.text }]}>{coins ?? '—'}</Text>
            </View>
            <Btn busy={signBusy} disabled={signed === true} onPress={doSign}>
              {signed === true ? '今日已签到' : '每日签到'}
            </Btn>
          </View>
        </Card>

        <Card>
          <Text style={[styles.section, { color: t.text }]}>可用优惠券 ({coupons.length})</Text>
          {coupons.map((coupon) => (
            <View key={coupon.id} style={styles.couponRow}>
              <Text style={{ color: t.text }}>{coupon.title}</Text>
              <Price>
                {coupon.amountOffCents > 0 ? `−¥${(coupon.amountOffCents / 100).toFixed(2)}` : `${coupon.percentOff}% off`}
              </Price>
            </View>
          ))}
          {coupons.length === 0 && <Muted>暂无可用优惠券</Muted>}
        </Card>

        <View style={styles.entryRow}>
          <Btn tone="plain" style={{ flex: 1 }} onPress={() => Taro.navigateTo({ url: '/pages/opening/opening' })}>
            我的奖品
          </Btn>
          <Btn tone="plain" style={{ flex: 1 }} onPress={() => Taro.navigateTo({ url: '/pages/order/order' })}>
            我的订单
          </Btn>
        </View>
        <Btn tone="danger" onPress={() => { logout(); setProfile(null); setError('未登录') }}>
          退出登录
        </Btn>
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  name: { fontSize: 20, fontWeight: '700' },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 6 },
  coins: { fontSize: 26, fontWeight: '700' },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  couponRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 10, minHeight: 44 },
  entryRow: { flexDirection: 'row', gap: 8, marginBottom: 12 }
})
