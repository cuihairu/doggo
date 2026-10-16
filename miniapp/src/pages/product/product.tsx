import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Image, Button } from '@tarojs/components'
import Taro, { useDidShow } from '@tarojs/taro'
import {
  createOrder, fetchCoinBalance, fetchMyCoupons, fetchProduct, fetchProfile, payOrder,
  MyCoupon, ProductDetail
} from '../../api/endpoints'
import { ApiError, getAccessToken, trackEvent, yuan } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'
import { ErrorBox, Skeleton } from '../../states'

/** Prototype wording for the plan rows: 单开 / 三开 / 十开 rather than "N连开". */
const QUANTITY_TEXT: Record<number, string> = {
  1: '单开',
  2: '双开',
  3: '三开',
  4: '四开',
  5: '五开',
  6: '六开',
  7: '七开',
  8: '八开',
  9: '九开',
  10: '十开'
}

function quantityLabel(quantity: number): string {
  return QUANTITY_TEXT[quantity] ?? `${quantity}连开`
}

/** Product detail: contents, plans with option prices, and the buy → pay → results flow. */
export default function ProductPage() {
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

  const load = useCallback(() => {
    const params = Taro.getCurrentInstance().router?.params ?? {}
    if (!params.id) return
    fetchProduct(params.id).then(setDetail).catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
    // Funnel step 1: the detail page rendered with a concrete product (design §3.2).
    trackEvent('page_view_product', { productId: params.id, source: 'direct' }, `/pages/product/product?id=${params.id}`)
  }, [])

  const loadCoupons = useCallback(() => {
    if (!getAccessToken()) return
    // Coins are read for the deduction chips; a failed read just hides them.
    fetchCoinBalance()
      .then((coins) => {
        setCoinBalance(coins.balance)
        setCoinCents((prev) => Math.min(prev, coins.balance))
      })
      .catch(() => null)
    fetchMyCoupons()
      .then((all) => setCoupons(all.filter((c) => c.status === 0 && (!c.validToUtc || new Date(c.validToUtc) > new Date()))))
      .catch(() => setCoupons([]))
  }, [])

  useEffect(() => {
    load()
    loadCoupons()
  }, [load, loadCoupons])

  // Reload after the login round-trip: returning from another page keeps this
  // screen mounted, so the coupon list would otherwise stay from guest state.
  useDidShow(() => {
    loadCoupons()
  })

  async function buy(target: { planTitle?: string; planId?: string; optionId?: string; priceCents: number; canUseCoupon?: boolean }) {
    if (!getAccessToken()) {
      Taro.showToast({ title: '请先登录', icon: 'none' })
      Taro.navigateTo({ url: '/pages/login/login' })
      return
    }
    // The server validates the coupon (threshold, plan eligibility) and rejects
    // the order if it does not apply; an empty selection must be undefined, not ''.
    const useCoupon = target.canUseCoupon !== false && couponId ? couponId : undefined
    // Coin spend is 1 coin = 1 cent off and the server rejects a short balance.
    // Confirmation step in place of a standalone checkout page: the miniapp has
    // no order route, so the order summary is confirmed in a modal before pay.
    const summary = [
      detail!.name,
      `套餐：${target.planTitle ?? '单开'}`,
      '支付方式：支付宝（模拟）',
      coinChoice > 0 ? `金币抵扣：${coinChoice} 金币（-¥${yuan(coinChoice)}）` : '',
      useCoupon ? `优惠券：${coupons.find((c) => c.id === couponId)?.title ?? ''}` : '',
      `应付 ¥${yuan(target.priceCents)}`,
      '优惠/抵扣以服务端核算为准',
      '生成订单后 15 分钟内完成支付'
    ].filter(Boolean).join('\n')
    const confirmed = await Taro.showModal({
      title: '确认订单',
      content: summary,
      confirmText: '去支付',
      cancelText: '再看看'
    })
    if (!confirmed.confirm) return
    // Funnel step 2: plan/quantity decided, buy confirmed (design §3.1).
    trackEvent('checkout_intent', { productId: detail!.id, planId: target.planId ?? null, optionId: target.optionId ?? null, quantity: 1 })
    setBusy(true)
    try {
      const profile = await fetchProfile()
      const order = await createOrder(profile.id, detail!.id, target.planId, target.optionId, useCoupon, 1, coinChoice)
      await payOrder(order.id)
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
      <ThemeRoot>
        <ErrorBox text={error} onRetry={load} />
      </ThemeRoot>
    )
  }
  if (!detail) {
    return (
      <ThemeRoot>
        <Skeleton cards={3} />
      </ThemeRoot>
    )
  }

  return (
    <ThemeRoot>
      <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      <View className="card">
        {detail.iconUrl && <Image src={detail.iconUrl} style={{ width: '120rpx', height: '120rpx' }} mode="aspectFill" />}
        <Text style={{ fontSize: '34rpx', fontWeight: 700 }}>{detail.name}</Text>
        <View className="row" style={{ gap: '16rpx', marginTop: '8rpx' }}>
          <Text className="price" style={{ fontSize: '38rpx' }}>¥{yuan(detail.finalPriceCents)}</Text>
          {detail.regularPriceCents > detail.finalPriceCents && (
            <Text className="muted price-strike">¥{yuan(detail.regularPriceCents)}</Text>
          )}
        </View>
      </View>

      {detail.plans?.map((plan) => (
        <View key={plan.id} className="card">
          <Text style={{ fontWeight: 700 }}>{plan.title}</Text>
          <View className="row" style={{ flexWrap: 'wrap', marginTop: '12rpx' }}>
            {(plan.options.length > 0 ? plan.options : [{ id: '', quantity: plan.minQuantity, priceCents: detail.finalPriceCents, upliftBps: 0 }])
              .map((option) => (
                <Button
                  key={option.id || plan.id}
                  size="mini"
                  className="btn-primary"
                  disabled={busy}
                  style={{ margin: '8rpx' }}
                  onClick={() => buy({ planTitle: plan.title, planId: plan.id, optionId: option.id || undefined, priceCents: option.priceCents, canUseCoupon: plan.canUseCoupon })}
                >
                  {quantityLabel(option.quantity)} ¥{yuan(option.priceCents)}
                </Button>
              ))}
          </View>
        </View>
      ))}

      {detail.plans?.length === 0 && (
        <View className="card">
          <Button className="btn-primary" disabled={busy} onClick={() => buy({ priceCents: detail.finalPriceCents })}>单开 ¥{yuan(detail.finalPriceCents)}</Button>
        </View>
      )}

      {coinChips.length > 0 && (
        <View className="card">
          <View className="row" style={{ justifyContent: 'space-between' }}>
            <Text style={{ fontWeight: 700 }}>金币抵扣</Text>
            <Text className="muted">余额 {coinBalance}</Text>
          </View>
          <View className="filter-row">
            <Text className={`filter-chip${coinCents === 0 ? ' on' : ''}`} onClick={() => setCoinCents(0)}>不使用</Text>
            {coinChips.map((step) => (
              <Text key={step} className={`filter-chip${coinCents === step ? ' on' : ''}`} onClick={() => setCoinCents(step)}>{step} 金币</Text>
            ))}
          </View>
          <Text className="muted" style={{ marginTop: '8rpx' }}>1 金币抵 1 分，余额不足时以服务端校验为准</Text>
        </View>
      )}

      {coupons.length > 0 && (
        <View className="card">
          <Text style={{ fontWeight: 700 }}>优惠券</Text>
          <View className="filter-row">
            <Text className={`filter-chip${couponId === '' ? ' on' : ''}`} onClick={() => setCouponId('')}>不使用</Text>
            {coupons.map((coupon) => (
              <Text
                key={coupon.id}
                className={`filter-chip${couponId === coupon.id ? ' on' : ''}`}
                onClick={() => setCouponId(coupon.id)}
              >
                {coupon.title} {coupon.type === 0 ? `-¥${yuan(coupon.amountOffCents)}` : `${coupon.percentOff}%`}{coupon.minOrderCents > 0 ? `（满¥${yuan(coupon.minOrderCents)}可用）` : ''}
              </Text>
            ))}
          </View>
          <Text className="muted" style={{ marginTop: '8rpx' }}>下单时自动核销，不适用套餐由服务端校验</Text>
        </View>
      )}

      <View className="card">
        <View className="row" style={{ justifyContent: 'space-between' }}>
          <Text style={{ fontWeight: 700 }}>包含内容（{(detail.contents ?? []).length}）</Text>
          <Text className="muted">按真实掉落</Text>
        </View>
        {(detail.contents ?? []).length > 0 ? (
          <View className="loot-grid">
            {(detail.contents ?? []).map((content) => (
              <View key={content.id} className="loot-cell" style={{ borderColor: content.color || undefined }}>
                {content.imageUrl && <Image src={content.imageUrl} style={{ width: '72rpx', height: '72rpx' }} mode="aspectFit" />}
                <Text className="loot-name" style={{ color: content.color || undefined }}>{content.title}</Text>
                <Text className="loot-meta">★{content.quality}</Text>
                <Text className="loot-meta">¥{yuan(content.referencePriceCents)}</Text>
              </View>
            ))}
          </View>
        ) : (
          <Text className="muted">暂无物品数据</Text>
        )}
      </View>
      <View style={{ height: '140rpx' }} />
      </ScrollView>

      <View className="buybar">
        <View>
          <Text className="loot-meta">最低单开</Text>
          <Text className="price" style={{ fontSize: '36rpx' }}>¥{yuan(detail.finalPriceCents)}</Text>
        </View>
        <Button className="btn-primary" disabled={busy} onClick={() => buy({ priceCents: detail.finalPriceCents })}>
          立即开箱
        </Button>
      </View>
    </ThemeRoot>
  )
}
