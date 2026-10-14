import { useCallback, useEffect, useState } from 'react'
import { Image, Linking, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchBanners, fetchNews, fetchMarket, fetchNotices, ApiError, BannerView, MarketItemView, NewsItemView, NoticeView, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

/** Home: quick entries, market glance and the news feed (cached on failure). */
export default function IndexPage() {
  const t = useTheme()
  const [news, setNews] = useState<NewsItemView[]>([])
  const [market, setMarket] = useState<MarketItemView[]>([])
  const [notices, setNotices] = useState<NoticeView[]>([])
  const [banners, setBanners] = useState<BannerView[]>([])
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
    fetchNotices().then(setNotices).catch(() => {})
    // Home carousel: operators publish banners with position "home".
    fetchBanners('home').then(setBanners).catch(() => {})
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
        {banners.length > 0 ? (
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.bannerBar}>
            {banners.map((banner) => (
              <View
                key={banner.id}
                style={[styles.banner, { backgroundColor: t.card, borderColor: t.border }]}
                onStartShouldSetResponder={() => true}
                onResponderRelease={() => {
                  // Empty link = decorative banner; only open real http(s) targets.
                  if (/^https?:\/\//.test(banner.linkUrl)) Linking.openURL(banner.linkUrl).catch(() => {})
                }}
              >
                {banner.imageUrl ? <Image source={{ uri: banner.imageUrl }} style={styles.bannerImage} /> : null}
                <Text numberOfLines={2} style={[styles.bannerTitle, { color: t.text }]}>{banner.title}</Text>
              </View>
            ))}
          </ScrollView>
        ) : null}

        <View style={styles.grid}>
          {entries.map((entry) => (
            <Btn key={entry.url} tone="plain" onPress={() => Taro.navigateTo({ url: entry.url })} style={styles.gridBtn}>
              {entry.label}
            </Btn>
          ))}
        </View>

        {stale ? <Msg text="网络不佳，正在展示缓存内容" /> : null}
        {error && !stale ? <Msg text={error} onRetry={load} /> : null}

        {notices.length > 0 ? (
          <Card>
            {notices.map((notice, index) => (
              <Muted key={notice.id} style={index > 0 ? { marginTop: 8 } : undefined}>
                公告 {notice.title}：{notice.content}
              </Muted>
            ))}
          </Card>
        ) : null}

        <Card>
          <Text style={[styles.section, { color: t.text }]}>行情速览</Text>
          {market.length === 0 && <Muted>暂无行情数据</Muted>}
          {market.map((item) => (
            <View key={item.id} style={styles.row}>
              <Text numberOfLines={1} style={[styles.rowTitle, { color: t.text, flex: 1 }]}>{item.marketHashName}</Text>
              <View style={{ alignItems: 'flex-end' }}>
                {item.priceCents != null && <Price>¥{yuan(item.priceCents)}</Price>}
                {item.changeBps != null ? (
                  <Text style={{ fontSize: 12, color: item.changeBps >= 0 ? t.danger : t.success }}>
                    {item.changeBps >= 0 ? '+' : ''}{(item.changeBps / 100).toFixed(2)}%
                  </Text>
                ) : null}
              </View>
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
  bannerBar: { flexGrow: 0, marginBottom: 12 },
  banner: { width: 220, borderRadius: 10, borderWidth: StyleSheet.hairlineWidth, padding: 10, marginRight: 10 },
  bannerImage: { width: '100%', height: 110, borderRadius: 6 },
  bannerTitle: { fontSize: 14, fontWeight: '600', marginTop: 6 },
  gridBtn: { flexGrow: 1, minWidth: '30%' },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 6 },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 8 },
  rowTitle: { fontSize: 14 },
  newsTitle: { fontSize: 15, fontWeight: '600' }
})
