import { useCallback, useEffect, useState } from 'react'
import { Image, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchProducts, ApiError, ProductView, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

const PAGE_SIZE = 20

/** On-shelf products list with search and load-more; tap a crate to open its plan list. */
export default function ProductsPage() {
  const t = useTheme()
  const [items, setItems] = useState<ProductView[]>([])
  const [total, setTotal] = useState(0)
  const [loaded, setLoaded] = useState(0)
  const [searchInput, setSearchInput] = useState('')
  const [q, setQ] = useState('')
  const [stale, setStale] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const loadPage = useCallback((page: number, append: boolean, query: string) => {
    setLoading(true)
    setStale(false)
    fetchProducts(page, { q: query })
      .then((result) => {
        const shelf = result.items.filter((p) => p.status === 3)
        setItems((prev) => (append ? [...prev, ...shelf] : shelf))
        setTotal(result.total)
        // Count rows fetched from the server, not the on-shelf subset: total
        // also covers self-open/group-open products, so the subset would
        // disagree with it and "load more" would refetch the last page.
        setLoaded((prev) => (append ? prev + result.items.length : result.items.length))
        saveCache(`doggo.cache.products.${query}`, shelf)
        setError('')
      })
      .catch((e) => {
        const cached = loadCache<ProductView[]>(`doggo.cache.products.${query}`)
        if (cached?.length) {
          setItems(cached)
          setStale(true)
        } else {
          setError(e instanceof ApiError ? e.message : '加载失败')
        }
      })
      .finally(() => setLoading(false))
  }, [])

  // Search changes restart from page 1.
  useEffect(() => {
    loadPage(1, false, q)
  }, [loadPage, q])

  const busy = loading

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        <Card>
          <TextInput
            placeholder="搜索商品名称"
            placeholderTextColor={t.muted}
            value={searchInput}
            onChangeText={setSearchInput}
            returnKeyType="search"
            onSubmitEditing={() => setQ(searchInput.trim())}
            autoCapitalize="none"
            style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          <Btn disabled={!searchInput.trim()} onPress={() => setQ(searchInput.trim())}>搜索</Btn>
        </Card>
        {stale ? <Msg text="网络不佳，正在展示缓存内容" /> : null}
        {error && !stale ? <Msg text={error} onRetry={() => loadPage(1, false, q)} /> : null}
        {items.length === 0 && !error && loading ? <Msg text="加载中…" /> : null}
        {items.length === 0 && !error && !loading ? <Msg text="没有符合条件的商品" /> : null}
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
        {items.length > 0 && loaded < total ? (
          <Btn busy={busy} disabled={busy} onPress={() => loadPage(Math.floor(loaded / PAGE_SIZE) + 1, true, q)}>
            {busy ? '加载中…' : `加载更多（已加载 ${loaded}/${total}）`}
          </Btn>
        ) : null}
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  input: { borderWidth: 1, borderRadius: 8, paddingHorizontal: 10, paddingVertical: 8, fontSize: 14, marginBottom: 10 },
  touchRow: { flexDirection: 'row', alignItems: 'center', minHeight: 56 },
  icon: { width: 44, height: 44, borderRadius: 6 },
  name: { fontSize: 15, fontWeight: '600' }
})
