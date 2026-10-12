import { useCallback, useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, TextInput, View } from 'react-native'
import Taro, { useDidShow } from '@tarojs/taro'
import {
  fetchCoinBalance, fetchCoinLogs, fetchMyCoupons, fetchProfile, fetchSignCycle,
  fetchTradeUrls, addTradeUrl, fetchClaimableCoupons, claimCoupon, fetchInviteRewards,
  logout, redeemCode, signToday,
  ApiError, getAccessToken, ClaimableCoupon, CoinLogEntry, Profile, TradeUrlEntry, InviteRewardView
} from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
import { useTheme } from '../../theme'

/** User center: identity, coins, coupons, weekly sign-in, entries. */
const COIN_LOG_TYPE: Record<number, string> = { 0: '发放', 1: '消费', 2: '退款', 3: '过期' }

export default function UserPage() {
  const t = useTheme()
  const [profile, setProfile] = useState<Profile | null>(null)
  const [coins, setCoins] = useState<number | null>(null)
  const [coupons, setCoupons] = useState<{ id: string; title: string; amountOffCents: number; percentOff: number }[]>([])
  const [signed, setSigned] = useState<boolean | null>(null)
  const [error, setError] = useState('')
  const [signBusy, setSignBusy] = useState(false)
  const [redeemInput, setRedeemInput] = useState('')
  const [redeemBusy, setRedeemBusy] = useState(false)
  const [tradeUrls, setTradeUrls] = useState<TradeUrlEntry[]>([])
  const [tradeInput, setTradeInput] = useState('')
  const [tradeBusy, setTradeBusy] = useState(false)
  const [coinLogs, setCoinLogs] = useState<CoinLogEntry[]>([])
  const [claimable, setClaimable] = useState<ClaimableCoupon[]>([])
  const [invites, setInvites] = useState<{ total: number; totalReward: number; items: InviteRewardView[] } | null>(null)

  const load = useCallback(() => {
    if (!getAccessToken()) {
      setError('未登录')
      return
    }
    setError('')
    fetchProfile()
      .then((p) => {
        setProfile(p); setError('')
        fetchTradeUrls(p.id).then(setTradeUrls).catch(() => {})
      })
      .catch((e) => {
        if (e instanceof ApiError && e.status === 401) {
          setError('未登录')
          return
        }
        setError(e instanceof ApiError ? e.message : '加载失败')
      })
    fetchCoinBalance().then((b) => setCoins(b.balance)).catch(() => {})
    fetchCoinLogs(1, 5).then((page) => setCoinLogs(page.items)).catch(() => {})
    // Unused = 0 / Used = 1 / Expired = 2 — "available" means unused and unexpired.
    fetchMyCoupons()
      .then((rows) => setCoupons(rows.filter((c) => c.status === 0 && (!c.validToUtc || new Date(c.validToUtc) > new Date()))))
      .catch(() => {})
    fetchSignCycle().then((cycle) => setSigned(cycle.todaySigned)).catch(() => {})
    fetchClaimableCoupons().then(setClaimable).catch(() => {})
    fetchInviteRewards(1, 10).then(setInvites).catch(() => {})
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

  async function doRedeem() {
    const code = redeemInput.trim()
    if (!code) return
    setRedeemBusy(true)
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
    } finally {
      setRedeemBusy(false)
    }
  }

  async function doAddTradeUrl() {
    const url = tradeInput.trim()
    if (!url || !profile) return
    setTradeBusy(true)
    try {
      await addTradeUrl(profile.id, url)
      setTradeInput('')
      setTradeUrls(await fetchTradeUrls(profile.id))
      Taro.showToast({ title: '已保存', icon: 'success' })
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '保存失败', icon: 'none' })
    } finally {
      setTradeBusy(false)
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

        {coinLogs.length > 0 ? (
          <Card>
            <Text style={[styles.section, { color: t.text }]}>金币明细</Text>
            {coinLogs.slice(0, 5).map((log) => (
              <View key={log.id} style={styles.couponRow}>
                <View style={{ flex: 1 }}>
                  <Text style={{ color: t.text }}>{COIN_LOG_TYPE[log.type] ?? log.type}</Text>
                  <Muted>{log.createdAtUtc.slice(0, 10)}</Muted>
                </View>
                <Text style={{ color: log.delta >= 0 ? t.primary : t.danger, fontWeight: '700' }}>
                  {log.delta >= 0 ? `+${log.delta}` : log.delta}
                </Text>
              </View>
            ))}
          </Card>
        ) : null}

        <Card>
          <Text style={[styles.section, { color: t.text }]}>Steam 交易链接</Text>
          {tradeUrls.map((link) => (
            <View key={link.id} style={styles.couponRow}>
              <Text style={{ color: t.text, flex: 1, marginRight: 8 }} numberOfLines={1}>
                {link.isPreferred ? '★ ' : ''}{link.url}
              </Text>
              <Muted>{link.status === 1 ? '已验证' : link.status === 2 ? '无效' : '未验证'}</Muted>
            </View>
          ))}
          {tradeUrls.length === 0 && <Muted>尚未绑定，饰品取回前需要</Muted>}
          <TextInput
            placeholder="粘贴 Steam 交易链接"
            placeholderTextColor={t.muted}
            value={tradeInput}
            onChangeText={setTradeInput}
            autoCapitalize="none"
            style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          <Btn busy={tradeBusy} disabled={!tradeInput.trim()} onPress={doAddTradeUrl}>保存</Btn>
        </Card>

        <Card>
          <Text style={[styles.section, { color: t.text }]}>兑换码</Text>
          <TextInput
            placeholder="输入兑换码"
            placeholderTextColor={t.muted}
            value={redeemInput}
            onChangeText={setRedeemInput}
            autoCapitalize="none"
            style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          <Btn busy={redeemBusy} disabled={!redeemInput.trim()} onPress={doRedeem}>兑换</Btn>
        </Card>

        <Card>
          <Text style={[styles.section, { color: t.text }]}>邀请返利</Text>
          <Muted>已成功邀请 {invites?.total ?? 0} 人，累计奖励 {invites?.totalReward ?? 0} 金币</Muted>
          {invites && invites.items.length === 0 ? (
            <Muted>还没有返利记录，把邀请码分享给好友即可</Muted>
          ) : (
            invites?.items.map((invite) => (
              <View key={invite.id} style={styles.couponRow}>
                <Text style={{ color: t.text }}>{invite.inviteeNickname || '—'}</Text>
                <Text style={{ color: t.primary, fontWeight: '700' }}>+{invite.coinReward}</Text>
              </View>
            ))
          )}
        </Card>

        <Card>
          <Text style={[styles.section, { color: t.text }]}>领券中心</Text>
          {claimable.map((coupon) => (
            <View key={coupon.id} style={styles.couponRow}>
              <View style={{ flex: 1 }}>
                <Text style={{ color: t.text }}>{coupon.title}</Text>
                <Muted>满 ¥{(coupon.minOrderCents / 100).toFixed(2)} 可用</Muted>
              </View>
              <Btn tone="plain" onPress={() => doClaim(coupon.id)}>领取</Btn>
            </View>
          ))}
          {claimable.length === 0 && <Muted>暂无可领优惠券</Muted>}
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
  input: { borderWidth: 1, borderRadius: 8, paddingHorizontal: 12, minHeight: 44, marginBottom: 12 },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  couponRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 10, minHeight: 44 },
  entryRow: { flexDirection: 'row', gap: 8, marginBottom: 12 }
})
