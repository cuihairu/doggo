import { useEffect, useState } from 'react'
import { View, Text, ScrollView } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchNews, fetchMarket, NewsItemView, MarketItemView } from '../../api/endpoints'
import { ApiError } from '../../api/client'

/** Home: news feed and a live market glance. */
export default function IndexPage() {
  const [news, setNews] = useState<NewsItemView[]>([])
  const [market, setMarket] = useState<MarketItemView[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    fetchNews(1).then((page) => { setNews(page.items); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
    fetchMarket(1).then((page) => setMarket(page.items.slice(0, 10))).catch(() => {})
  }, [])

  return (
    <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      {error && <View className="card"><Text className="muted">{error}</Text></View>}

      <View className="card">
        <Text style={{ fontWeight: 700 }}>行情速览</Text>
        {market.length === 0 && <Text className="muted">暂无行情数据</Text>}
        {market.map((item) => (
          <View key={item.id} className="row" style={{ justifyContent: 'space-between', marginTop: '12rpx' }}>
            <Text style={{ flex: 1 }} numberOfLines={1}>{item.marketHashName}</Text>
            {item.priceCents != null && <Text className="price">¥{(item.priceCents / 100).toFixed(2)}</Text>}
          </View>
        ))}
      </View>

      <View className="card">
        <Text style={{ fontWeight: 700 }}>资讯</Text>
        {news.length === 0 && <Text className="muted">加载中…</Text>}
        {news.map((item) => (
          <View key={item.id} style={{ marginTop: '20rpx' }} onClick={() => Taro.navigateTo({ url: `/pages/newsdetail/newsdetail?id=${item.id}` })}>
            <Text style={{ fontWeight: 600 }}>{item.title}</Text>
            <View className="row" style={{ justifyContent: 'space-between', marginTop: '8rpx' }}>
              <Text className="muted">{item.source} · {item.category}</Text>
              <Text className="muted">{String(item.publishedAtUtc ?? '').slice(0, 10)}</Text>
            </View>
          </View>
        ))}
      </View>
    </ScrollView>
  )
}
