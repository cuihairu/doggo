import { useEffect, useState } from 'react'
import { Image, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro, { useRouter } from '@tarojs/taro'
import {
  createOrder, fetchMyCoupons, fetchProduct, fetchProfile, payOrder,
  ApiError, getAccessToken, kvGet, kvSet, MyCoupon, ProductDetail, yuan
} from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
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
  }

  useEffect(() => {
    const params = (Taro.getCurrentInstance().router?.params ?? {}) as { id?: string }
    load(params.id)
    loadCoupons()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router.params])

  function rememberOrder(id: string) {
    const list = kvGet<string[]>('doggo.orderIds') ?? []
    kvSet('doggo.orderIds', [id, ...list.filter((x) => x !== id)].slice(0, 10))
    kvSet('doggo.lastOrderId', id)
  }

  async function buy(planId?: string, optionId?: string, allowCoupon = true) {
    if (!getAccessToken()) {
      Taro.showToast({ title: '请先登录', icon: 'none' })
      Taro.navigateTo({ url: '/pages/login/login' })
      return
    }
    setBusy(true)
    try {
      const profile = await fetchProfile()
      // The server validates the coupon (threshold, plan eligibility) and
      // rejects the order with a message if it does not apply.
      const useCoupon = allowCoupon ? couponId : undefined
      const order = await createOrder(profile.id, detail!.id, planId, optionId, useCoupon)
      await payOrder(order.id)
      rememberOrder(order.id)
      if (useCoupon) {
        setCouponId('')
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
          <Msg text="加载中…" />
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
            <Btn busy={busy} onPress={() => buy()}>单开 ¥{yuan(detail.finalPriceCents)}</Btn>
          </Card>
        )}

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
          <Text style={[styles.section, { color: t.text }]}>箱内物品</Text>
          {(detail.contents ?? []).map((content) => (
            <View key={content.id} style={styles.row}>
              <Text numberOfLines={1} style={{ flex: 1, color: content.color || t.text }}>{content.title}</Text>
              <Muted>¥{yuan(content.referencePriceCents)}</Muted>
            </View>
          ))}
          {(detail.contents ?? []).length === 0 && <Muted>暂无物品数据</Muted>}
        </Card>
      </ScrollView>
    </View>
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
  }
})
