import { useEffect, useState } from 'react'
import { View, Text, ScrollView, Image } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchBanners, fetchNews, fetchMarket, fetchNotices, BannerView, NewsItemView, MarketItemView, NoticeView } from '../../api/endpoints'
import { ApiError } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'

/** Home: banner carousel, notices, news feed and a live market glance. */
export default function IndexPage() {
  const [news, setNews] = useState<NewsItemView[]>([])
  const [market, setMarket] = useState<MarketItemView[]>([])
  const [notices, setNotices] = useState<NoticeView[]>([])
  const [banners, setBanners] = useState<BannerView[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    fetchNews(1).then((page) => { setNews(page.items); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
    fetchMarket(1).then((page) => setMarket(page.items.slice(0, 10))).catch(() => {})
    fetchNotices().then(setNotices).catch(() => {})
    // Home carousel: operators publish banners with position "home".
    fetchBanners('home').then(setBanners).catch(() => {})
  }, [])

  return (
    <ThemeRoot>
      <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      {banners.length > 0 && (
        <ScrollView scrollX enableFlex showScrollbar={false} className="banner-bar">
          {banners.map((banner) => (
            <View
              key={banner.id}
              className="banner"
              onClick={() => {
                // Mini programs cannot open external pages in a web-view unless the
                // domain is whitelisted: internal routes navigate, http links copy.
                if (banner.linkUrl.startsWith('/pages/')) {
                  Taro.navigateTo({ url: banner.linkUrl })
                } else if (/^https?:\/\//.test(banner.linkUrl)) {
                  Taro.setClipboardData({ data: banner.linkUrl })
                  Taro.showToast({ title: '链接已复制', icon: 'none' })
                }
              }}
            >
              {banner.imageUrl && <Image src={banner.imageUrl} className="banner-image" mode="aspectFill" />}
              <Text className="banner-title" numberOfLines={2}>{banner.title}</Text>
            </View>
          ))}
        </ScrollView>
      )}

      {error && <View className="card"><Text className="muted">{error}</Text></View>}

      {notices.length > 0 && (
        <View className="card">
          {notices.map((notice, index) => (
            <Text key={notice.id} className="muted" style={index > 0 ? { marginTop: '10rpx' } : undefined}>
              公告 {notice.title}：{notice.content}
            </Text>
          ))}
        </View>
      )}

      <View className="card">
        <Text style={{ fontWeight: 700 }}>行情速览</Text>
        {market.length === 0 && <Text className="muted">暂无行情数据</Text>}
        {market.map((item) => (
          <View key={item.id} className="row" style={{ justifyContent: 'space-between', marginTop: '12rpx' }}>
            <Text style={{ flex: 1 }} numberOfLines={1}>{item.marketHashName}</Text>
            <View style={{ textAlign: 'right' }}>
              {item.priceCents != null && <Text className="price">¥{(item.priceCents / 100).toFixed(2)}</Text>}
              {item.changeBps != null && (
                <Text className={item.changeBps >= 0 ? 'change-up' : 'change-down'}>
                  {item.changeBps >= 0 ? '+' : ''}{(item.changeBps / 100).toFixed(2)}%
                </Text>
              )}
            </View>
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
    </ThemeRoot>
  )
}
