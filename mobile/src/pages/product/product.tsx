import { useEffect, useState } from 'react'
import { Image, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro, { useDidShow, useRouter } from '@tarojs/taro'
import {
  createOrder, fetchCoinBalance, fetchMyCoupons, fetchProduct, fetchProfile, payOrder,
  ApiError, getAccessToken, kvGet, kvSet, MyCoupon, ProductDetail, trackEvent, yuan
} from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price, Skeleton } from '../../ui'
import { useTheme } from '../../theme'

/** Product detail: contents, plans with option prices, and the buy → pay → results flow. */
export default function ProductPage() {
  const t = useTheme()
  const router = useRouter()
  const [detail, setDetail] = useState<ProductDetail | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [coupons, setCoupons] = useState<MyCoupon[]>([])
  const [couponId, setCouponId] = useState('')
  const [coinBalance, setCoinBalance] = useState(0)
  const [coinCents, setCoinCents] = useState(0)

  /** Coin chips are fixed steps; the top one is capped at the real balance. */
  const coinSteps = [50, 200, 500, 2000]
  const coinChips = coinSteps.filter((step) => step <= coinBalance)
  const coinChoice = Math.min(coinCents, coinBalance)

  async function load(id?: string) {
    if (!id) return
    try {
      setDetail(await fetchProduct(id))
      setError('')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '加载失败')
    }
  }

  async function loadCoupons() {
    if (!getAccessToken()) return
    const usable = await fetchMyCoupons()
      .catch(() => [])
    setCoupons(usable.filter((c) => c.status === 0 && (!c.validToUtc || new Date(c.validToUtc) > new Date())))
    // Coins are read for the deduction chips; a failed read just hides them.
    const coins = await fetchCoinBalance().catch(() => null)
    if (coins) {
      setCoinBalance(coins.balance)
      setCoinCents((prev) => Math.min(prev, coins.balance))
    }
  }

  useEffect(() => {
    const params = (Taro.getCurrentInstance().router?.params ?? {}) as { id?: string }
    load(params.id)
    loadCoupons()
    // Funnel step 1: the detail page rendered with a concrete product (design §3.2).
    if (params.id) {
      trackEvent('page_view_product', { productId: params.id, source: 'direct' }, `/pages/product/product?id=${params.id}`)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router.params])

  // Reload after the login round-trip: returning from another page keeps this
  // screen mounted, so the coupon list would otherwise stay from guest state.
  useDidShow(() => {
    loadCoupons()
  })

  function rememberOrder(id: string) {
    const list = kvGet<string[]>('doggo.orderIds') ?? []
    kvSet('doggo.orderIds', [id, ...list.filter((x) => x !== id)].slice(0, 10))
    kvSet('doggo.lastOrderId', id)
  }

  async function buy(planId?: string, optionId?: string, allowCoupon = true, quantity = 1) {
    if (!getAccessToken()) {
      Taro.showToast({ title: '请先登录', icon: 'none' })
      Taro.navigateTo({ url: '/pages/login/login' })
      return
    }
    // Funnel step 2: plan/quantity decided, buy confirmed (design §3.1).
    trackEvent('checkout_intent', { productId: detail?.id ?? '', planId: planId ?? null, optionId: optionId ?? null, quantity })
    setBusy(true)
    try {
      const profile = await fetchProfile()
      // The server validates the coupon (threshold, plan eligibility) and
      // rejects the order with a message if it does not apply. An empty
      // selection must be undefined, not '' — the API binds Guid? and an
      // empty string fails deserialization.
      const useCoupon = allowCoupon && couponId ? couponId : undefined
      // Coin spend is 1 coin = 1 cent off and the server rejects a short balance.
      const order = await createOrder(profile.id, detail!.id, planId, optionId, useCoupon, quantity, coinChoice)
      await payOrder(order.id)
      rememberOrder(order.id)
      if (useCoupon || coinChoice > 0) {
        setCouponId('')
        setCoinCents(0)
        loadCoupons()
      }
      Taro.showToast({ title: '已支付', icon: 'success' })
      Taro.navigateTo({ url: '/pages/opening/opening' })
    } catch (e) {
      Taro.showToast({ title: e instanceof ApiError ? e.message : '下单失败', icon: 'none' })
    } finally {
      setBusy(false)
    }
  }

  if (error) {
    return (
      <View style={[styles.page, { backgroundColor: t.bg }]}>
        <SafeTop style={{ backgroundColor: t.bg }} />
        <View style={{ padding: 16 }}>
          <Msg text={error} onRetry={() => load((Taro.getCurrentInstance().router?.params as { id?: string })?.id)} />
        </View>
      </View>
    )
  }
  if (!detail) {
    return (
      <View style={[styles.page, { backgroundColor: t.bg }]}>
        <SafeTop style={{ backgroundColor: t.bg }} />
        <View style={{ padding: 16 }}>
          <Skeleton cards={3} />
        </View>
      </View>
    )
  }

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        <Card>
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            {detail.iconUrl ? <Image source={{ uri: detail.iconUrl }} style={styles.icon} /> : null}
            <View style={{ flex: 1, marginHorizontal: 10 }}>
              <Text style={[styles.name, { color: t.text }]}>{detail.name}</Text>
              <Price>¥{yuan(detail.finalPriceCents)}</Price>
            </View>
          </View>
        </Card>

        {(detail.plans ?? []).map((plan) => (
          <Card key={plan.id}>
            <Text style={[styles.section, { color: t.text }]}>{plan.title}</Text>
            <View style={styles.wrap}>
              {(plan.options.length > 0
                ? plan.options
                : [{ id: '', quantity: plan.minQuantity, priceCents: detail.finalPriceCents, upliftBps: 0 }]
              ).map((option) => (
                <Btn key={option.id || plan.id} busy={busy} style={styles.optionBtn} onPress={() => buy(plan.id, option.id || undefined, plan.canUseCoupon)}>
                  {option.quantity}连开 ¥{yuan(option.priceCents)}
                </Btn>
              ))}
            </View>
          </Card>
        ))}

        {(detail.plans ?? []).length === 0 && (
          <Card>
            {(detail.quantityTiers ?? []).length > 0 && (
              <Text style={[styles.section, { color: t.text }]}>数量档</Text>
            )}
            <View style={styles.wrap}>
              <Btn busy={busy} style={styles.optionBtn} onPress={() => buy()}>
                单开 ¥{yuan(detail.finalPriceCents)}
              </Btn>
              {(detail.quantityTiers ?? []).map((tier) => (
                <Btn
                  key={tier.quantity}
                  busy={busy}
                  style={styles.optionBtn}
                  onPress={() => buy(undefined, undefined, true, tier.quantity)}
                >
                  {tier.quantity}个 ¥{yuan(tier.priceCents * tier.quantity)}
                </Btn>
              ))}
            </View>
            {(detail.quantityTiers ?? []).length > 0 && (
              <Muted>达到档位数量整单按档位单价计</Muted>
            )}
          </Card>
        )}

        {coinChips.length > 0 ? (
          <Card>
            <View style={styles.row}>
              <Text style={[styles.section, { color: t.text, marginBottom: 0 }]}>金币抵扣</Text>
              <Muted>余额 {coinBalance}</Muted>
            </View>
            <View style={[styles.wrap, { marginTop: 8 }]}>
              <Chip label="不使用" selected={coinCents === 0} color={t.muted} border={t.border} onPress={() => setCoinCents(0)} />
              {coinChips.map((step) => (
                <Chip
                  key={step}
                  label={`${step} 金币`}
                  selected={coinCents === step}
                  color={t.text}
                  border={coinCents === step ? t.primary : t.border}
                  onPress={() => setCoinCents(step)}
                />
              ))}
            </View>
            <Muted>1 金币抵 1 分，余额不足时以服务端校验为准</Muted>
          </Card>
        ) : null}

        {coupons.length > 0 ? (
          <Card>
            <Text style={[styles.section, { color: t.text }]}>优惠券</Text>
            <CouponRow
              label="不使用优惠券"
              selected={couponId === ''}
              color={t.muted}
              border={t.border}
              onPress={() => setCouponId('')}
            />
            {coupons.map((coupon) => (
              <CouponRow
                key={coupon.id}
                label={`${coupon.title} ${coupon.type === 0 ? `-¥${yuan(coupon.amountOffCents)}` : `${coupon.percentOff}%`}${coupon.minOrderCents > 0 ? `（满¥${yuan(coupon.minOrderCents)}可用）` : ''}`}
                selected={couponId === coupon.id}
                color={t.text}
                border={couponId === coupon.id ? t.primary : t.border}
                onPress={() => setCouponId(coupon.id)}
              />
            ))}
            <Muted>下单时自动核销，不适用套餐由服务端校验</Muted>
          </Card>
        ) : null}

        <Card>
          <View style={styles.row}>
            <Text style={[styles.section, { color: t.text, marginBottom: 0 }]}>包含内容（{(detail.contents ?? []).length}）</Text>
            <Muted>按真实掉落</Muted>
          </View>
          {(detail.contents ?? []).length > 0 ? (
            <View style={styles.lootGrid}>
              {(detail.contents ?? []).map((content) => (
                <View key={content.id} style={[styles.lootCell, { borderColor: content.color || t.border }]}>
                  {content.imageUrl ? <Image source={{ uri: content.imageUrl }} style={styles.lootIcon} /> : null}
                  <Text numberOfLines={1} style={[styles.lootName, { color: content.color || t.text }]}>{content.title}</Text>
                  <Text style={[styles.lootStars, { color: t.muted }]}>★{content.quality}</Text>
                  <Text style={[styles.lootPrice, { color: t.muted }]}>¥{yuan(content.referencePriceCents)}</Text>
                </View>
              ))}
            </View>
          ) : (
            <Muted>暂无物品数据</Muted>
          )}
        </Card>
      </ScrollView>

      <View style={[styles.buybar, { backgroundColor: t.card, borderTopColor: t.border }]}>
        <View style={{ flex: 1 }}>
          <Muted>最低单开</Muted>
          <Price>¥{yuan(detail.finalPriceCents)}</Price>
        </View>
        <Btn busy={busy} style={styles.buyBtn} onPress={() => buy()}>
          立即开箱
        </Btn>
      </View>
    </View>
  )
}

/** Selectable chip — same 44px touch target as CouponRow, check mark when picked. */
function Chip({ label, selected, color, border, onPress }: {
  label: string
  selected: boolean
  color: string
  border: string
  onPress: () => void
}) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [
        styles.chip,
        { borderColor: border },
        pressed && { opacity: 0.8 }
      ]}
    >
      <Text style={{ color, fontWeight: selected ? '700' : '400' }}>{selected ? `✓ ${label}` : label}</Text>
    </Pressable>
  )
}

/** Selectable coupon row — 44px touch target, check mark when picked. */
function CouponRow({ label, selected, color, border, onPress }: {
  label: string
  selected: boolean
  color: string
  border: string
  onPress: () => void
}) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [
        styles.couponRow,
        { borderColor: border },
        pressed && { opacity: 0.8 }
      ]}
    >
      <Text numberOfLines={1} style={{ flex: 1, color }}>{label}</Text>
      {selected ? <Text style={{ color, fontWeight: '700' }}>✓</Text> : null}
    </Pressable>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  icon: { width: 64, height: 64, borderRadius: 8 },
  name: { fontSize: 18, fontWeight: '700', marginBottom: 4 },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 8 },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  optionBtn: { paddingHorizontal: 12 },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 8, minHeight: 44 },
  couponRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    minHeight: 44,
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    marginTop: 8
  },
  chip: {
    minHeight: 44,
    justifyContent: 'center',
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 14
  },
  lootGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: 10 },
  lootCell: {
    width: '23%',
    minHeight: 92,
    borderWidth: 1,
    borderRadius: 8,
    padding: 6,
    alignItems: 'center'
  },
  lootIcon: { width: 32, height: 32, borderRadius: 4 },
  lootName: { fontSize: 11, marginTop: 4 },
  lootStars: { fontSize: 11 },
  lootPrice: { fontSize: 10 },
  buybar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    paddingHorizontal: 16,
    paddingTop: 10,
    paddingBottom: 18,
    borderTopWidth: StyleSheet.hairlineWidth
  },
  buyBtn: { paddingHorizontal: 28 }
})
