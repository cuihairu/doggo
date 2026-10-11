import { useCallback, useEffect, useState } from 'react'
import { Image, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchProducts, ApiError, ProductView, yuan } from '@doggo/shared'
import { SafeTop, Card, Msg, Muted } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

/** On-shelf products list; tap a crate to open its plan list. */
export default function ProductsPage() {
  const t = useTheme()
  const [items, setItems] = useState<ProductView[]>([])
  const [stale, setStale] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => {
    setLoading(true)
    setStale(false)
    fetchProducts(1)
      .then((page) => {
        const shelf = page.items.filter((p) => p.status === 3)
        setItems(shelf)
        saveCache('doggo.cache.products', shelf)
        setError('')
      })
      .catch((e) => {
        const cached = loadCache<ProductView[]>('doggo.cache.products')
        if (cached?.length) {
          setItems(cached)
          setStale(true)
        } else {
          setError(e instanceof ApiError ? e.message : '加载失败')
        }
      })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        {stale ? <Msg text="网络不佳，正在展示缓存内容" /> : null}
        {error && !stale ? <Msg text={error} onRetry={load} /> : null}
        {items.length === 0 && !error && loading ? <Msg text="加载中…" /> : null}
        {items.map((item) => (
          <Card key={item.id}>
            <View
              style={styles.touchRow}
              onStartShouldSetResponder={() => true}
              onResponderRelease={() => Taro.navigateTo({ url: `/pages/product/product?id=${item.id}` })}
            >
              {item.iconUrl ? <Image source={{ uri: item.iconUrl }} style={styles.icon} /> : null}
              <View style={{ flex: 1, marginHorizontal: 10 }}>
                <Text style={[styles.name, { color: t.text }]} numberOfLines={1}>{item.name}</Text>
                <Muted>
                  {yuan(item.finalPriceCents)} 起
                  {item.regularPriceCents > item.finalPriceCents ? ` · 原价 ${yuan(item.regularPriceCents)}` : ''}
                </Muted>
              </View>
            </View>
          </Card>
        ))}
        <BtnRefresh onPress={load} />
      </ScrollView>
    </View>
  )
}

function BtnRefresh({ onPress }: { onPress: () => void }) {
  const t = useTheme()
  return (
    <Text onPress={onPress} style={{ color: t.primary, textAlign: 'center', padding: 12, minHeight: 44 }}>
      刷新
    </Text>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  touchRow: { flexDirection: 'row', alignItems: 'center', minHeight: 56 },
  icon: { width: 44, height: 44, borderRadius: 6 },
  name: { fontSize: 15, fontWeight: '600' }
})
