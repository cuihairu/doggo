import { useEffect, useState } from 'react'
import { View, Text, ScrollView } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchMarket, fetchMarketPrices, MarketItemView, PricePoint } from '../../api/endpoints'
import { ApiError, yuan } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'

interface ItemWithTrend {
  item: MarketItemView
  points: PricePoint[]
}

/** Market quotes with a per-item daily close trend. */
export default function MarketPage() {
  const [items, setItems] = useState<MarketItemView[]>([])
  const [trends, setTrends] = useState<Record<string, ItemWithTrend[]>>({})
  const [error, setError] = useState('')

  useEffect(() => {
    fetchMarket(1).then((page) => { setItems(page.items); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
  }, [])

  function change(points: PricePoint[]): { latest: number; pct: number | null } {
    if (points.length === 0) return { latest: 0, pct: null }
    const newestFirst = points.slice().reverse() // the API returns newest first
    const base = newestFirst[newestFirst.length - 1]?.closeCents ?? 0
    const pct = newestFirst.length < 2 || base <= 0
      ? null
      : ((newestFirst[0].closeCents - base) / base) * 100
    return { latest: newestFirst[0].closeCents, pct }
  }

  async function loadTrend(item: MarketItemView) {
    const result = await fetchMarketPrices(item.id).catch(() => null)
    if (!result) return
    setTrends((prev) => ({ ...prev, [item.id]: [{ item, points: result.points }] }))
  }

  return (
    <ThemeRoot>
      <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      {error && <View className="card"><Text className="muted">{error}</Text></View>}
      {items.length === 0 && <View className="card"><Text className="muted">加载中…</Text></View>}
      {items.map((item) => (
        <View key={item.id} className="card" onClick={() => loadTrend(item)}>
          <View className="row" style={{ justifyContent: 'space-between' }}>
            <Text style={{ flex: 1, fontWeight: 600 }} numberOfLines={1}>{item.marketHashName}</Text>
            <Text className="price">{item.priceCents != null ? `¥${yuan(item.priceCents)}` : '—'}</Text>
          </View>
          <Text className="muted">{item.classId}</Text>
        </View>
      ))}
      <View className="card">
        <Text style={{ fontWeight: 700 }}>近 30 日收盘</Text>
        {Object.entries(trends).map(([id, rows]) => {
          const { latest, pct } = change(rows[0].points)
          return (
            <View key={id} style={{ marginTop: '16rpx' }}>
              <Text>{rows[0].item.marketHashName}</Text>
              <Text className="muted">
                {latest > 0 ? `最新 ¥${yuan(latest)}${pct != null ? ` · 期间 ${pct.toFixed(1)}%` : ''}` : '暂无数据'}
              </Text>
            </View>
          )
        })}
        {Object.keys(trends).length === 0 && <Text className="muted">点击上方行情查看走势</Text>}
      </View>
      </ScrollView>
    </ThemeRoot>
  )
}
