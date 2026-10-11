import { useEffect, useState } from 'react'
import { Image, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro, { useRouter } from '@tarojs/taro'
import {
  createOrder, fetchProduct, fetchProfile, payOrder,
  ApiError, getAccessToken, ProductDetail, yuan
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

  async function load(id?: string) {
    if (!id) return
    try {
      setDetail(await fetchProduct(id))
      setError('')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '加载失败')
    }
  }

  useEffect(() => {
    const params = (Taro.getCurrentInstance().router?.params ?? {}) as { id?: string }
    load(params.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router.params])

  function rememberOrder(id: string) {
    try {
      const list = (Taro.getStorageSync('doggo.orderIds') as string[] | undefined) ?? []
      const next = [id, ...list.filter((x) => x !== id)].slice(0, 10)
      Taro.setStorageSync('doggo.orderIds', next)
      Taro.setStorageSync('doggo.lastOrderId', id)
    } catch {
      // storage unavailable — the order page just won't have history
    }
  }

  async function buy(planId?: string, optionId?: string) {
    if (!getAccessToken()) {
      Taro.showToast({ title: '请先登录', icon: 'none' })
      Taro.navigateTo({ url: '/pages/login/login' })
      return
    }
    setBusy(true)
    try {
      const profile = await fetchProfile()
      const order = await createOrder(profile.id, detail!.id, planId, optionId)
      await payOrder(order.id)
      rememberOrder(order.id)
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
                <Btn key={option.id || plan.id} busy={busy} style={styles.optionBtn} onPress={() => buy(plan.id, option.id || undefined)}>
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

const styles = StyleSheet.create({
  page: { flex: 1 },
  icon: { width: 64, height: 64, borderRadius: 8 },
  name: { fontSize: 18, fontWeight: '700', marginBottom: 4 },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 8 },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  optionBtn: { paddingHorizontal: 12 },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 8, minHeight: 44 }
})
