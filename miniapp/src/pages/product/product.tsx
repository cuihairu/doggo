import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Image, Button } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { createOrder, fetchProduct, fetchProfile, payOrder, ProductDetail } from '../../api/endpoints'
import { ApiError, getAccessToken, trackEvent, yuan } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'
import { ErrorBox, Skeleton } from '../../states'

/** Product detail: contents, plans with option prices, and the buy → pay → results flow. */
export default function ProductPage() {
  const [detail, setDetail] = useState<ProductDetail | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    const params = Taro.getCurrentInstance().router?.params ?? {}
    if (!params.id) return
    fetchProduct(params.id).then(setDetail).catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
    // Funnel step 1: the detail page rendered with a concrete product (design §3.2).
    trackEvent('page_view_product', { productId: params.id, source: 'direct' }, `/pages/product/product?id=${params.id}`)
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function buy(target: { planTitle?: string; planId?: string; optionId?: string; priceCents: number }) {
    if (!getAccessToken()) {
      Taro.showToast({ title: '请先登录', icon: 'none' })
      Taro.navigateTo({ url: '/pages/login/login' })
      return
    }
    // Confirmation step in place of a standalone checkout page: the miniapp has
    // no order route, so the order summary is confirmed in a modal before pay.
    const confirmed = await Taro.showModal({
      title: '确认订单',
      content: [
        detail!.name,
        `套餐：${target.planTitle ?? '单开'}`,
        '支付方式：支付宝（模拟）',
        `应付 ¥${yuan(target.priceCents)}`,
        '生成订单后 15 分钟内完成支付'
      ].join('\n'),
      confirmText: '去支付',
      cancelText: '再看看'
    })
    if (!confirmed.confirm) return
    // Funnel step 2: plan/quantity decided, buy confirmed (design §3.1).
    trackEvent('checkout_intent', { productId: detail!.id, planId: target.planId ?? null, optionId: target.optionId ?? null, quantity: 1 })
    setBusy(true)
    try {
      const profile = await fetchProfile()
      const order = await createOrder(profile.id, detail!.id, target.planId, target.optionId)
      await payOrder(order.id)
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
        <Text className="price">¥{yuan(detail.finalPriceCents)}</Text>
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
                  onClick={() => buy({ planTitle: plan.title, planId: plan.id, optionId: option.id || undefined, priceCents: option.priceCents })}
                >
                  {option.quantity}连开 ¥{yuan(option.priceCents)}
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

      <View className="card">
        <Text style={{ fontWeight: 700 }}>箱内物品</Text>
        {(detail.contents ?? []).map((content) => (
          <View key={content.id} className="row" style={{ justifyContent: 'space-between', marginTop: '10rpx' }}>
            <Text numberOfLines={1} style={{ flex: 1, color: content.color || undefined }}>{content.title}</Text>
            <Text className="muted">¥{yuan(content.referencePriceCents)}</Text>
          </View>
        ))}
        {(detail.contents ?? []).length === 0 && <Text className="muted">暂无物品数据</Text>}
      </View>
      </ScrollView>
    </ThemeRoot>
  )
}
