import { useCallback, useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchNews, fetchMarket, ApiError, MarketItemView, NewsItemView, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

/** Home: quick entries, market glance and the news feed (cached on failure). */
export default function IndexPage() {
  const t = useTheme()
  const [news, setNews] = useState<NewsItemView[]>([])
  const [market, setMarket] = useState<MarketItemView[]>([])
  const [stale, setStale] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => {
    setLoading(true)
    setStale(false)
    setError('')
    fetchNews(1)
      .then((page) => {
        setNews(page.items)
        saveCache('doggo.cache.news', page.items)
        setError('')
      })
      .catch((e) => {
        const cached = loadCache<NewsItemView[]>('doggo.cache.news')
        if (cached?.length) {
          setNews(cached)
          setStale(true)
        } else {
          setError(e instanceof ApiError ? e.message : '加载失败')
        }
      })
      .finally(() => setLoading(false))
    fetchMarket(1)
      .then((page) => {
        setMarket(page.items.slice(0, 5))
        saveCache('doggo.cache.market', page.items.slice(0, 5))
      })
      .catch(() => {
        const cached = loadCache<MarketItemView[]>('doggo.cache.market')
        if (cached?.length) setMarket(cached)
      })
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const entries = [
    { label: '商品开箱', url: '/pages/products/products' },
    { label: '行情', url: '/pages/market/market' },
    { label: '我的奖品', url: '/pages/opening/opening' },
    { label: '订单', url: '/pages/order/order' },
    { label: '个人中心', url: '/pages/user/user' },
    { label: '登录', url: '/pages/login/login' }
  ]

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        <View style={styles.grid}>
          {entries.map((entry) => (
            <Btn key={entry.url} tone="plain" onPress={() => Taro.navigateTo({ url: entry.url })} style={styles.gridBtn}>
              {entry.label}
            </Btn>
          ))}
        </View>

        {stale ? <Msg text="网络不佳，正在展示缓存内容" /> : null}
        {error && !stale ? <Msg text={error} onRetry={load} /> : null}

        <Card>
          <Text style={[styles.section, { color: t.text }]}>行情速览</Text>
          {market.length === 0 && <Muted>暂无行情数据</Muted>}
          {market.map((item) => (
            <View key={item.id} style={styles.row}>
              <Text numberOfLines={1} style={[styles.rowTitle, { color: t.text, flex: 1 }]}>{item.marketHashName}</Text>
              {item.priceCents != null && <Price>¥{yuan(item.priceCents)}</Price>}
            </View>
          ))}
          <Btn tone="plain" onPress={() => Taro.navigateTo({ url: '/pages/market/market' })} style={{ marginTop: 8 }}>
            查看全部行情
          </Btn>
        </Card>

        <Card>
          <Text style={[styles.section, { color: t.text }]}>资讯</Text>
          {news.length === 0 && loading && <Muted>加载中…</Muted>}
          {news.map((item) => (
            <View
              key={item.id}
              style={{ marginTop: 14 }}
              onStartShouldSetResponder={() => true}
              onResponderRelease={() => Taro.navigateTo({ url: `/pages/newsdetail/newsdetail?id=${item.id}` })}
            >
              <Text style={[styles.newsTitle, { color: t.text }]}>{item.title}</Text>
              <View style={styles.row}>
                <Muted>{item.source} · {item.category}</Muted>
                <Muted>{String(item.publishedAtUtc ?? '').slice(0, 10)}</Muted>
              </View>
            </View>
          ))}
        </Card>
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginBottom: 12 },
  gridBtn: { flexGrow: 1, minWidth: '30%' },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 6 },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 8 },
  rowTitle: { fontSize: 14 },
  newsTitle: { fontSize: 15, fontWeight: '600' }
})
