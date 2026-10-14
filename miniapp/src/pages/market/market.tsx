import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Input } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchMarket, fetchMarketPrices, MarketItemView, PricePoint } from '../../api/endpoints'
import { ApiError, yuan } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'
import { EmptyState, ErrorBox, Skeleton } from '../../states'

interface ItemWithTrend {
  item: MarketItemView
  points: PricePoint[]
}

// Mirrors the enums on the server (GameKind / MarketItemType) for the filters.
const GAMES = [
  { value: '', label: '全部游戏' },
  { value: '1', label: 'CS2' },
  { value: '2', label: 'Dota 2' }
]

const TYPES = [
  { value: '', label: '全部类型' },
  { value: '0', label: '武器箱' },
  { value: '1', label: '钥匙' },
  { value: '2', label: '饰品' },
  { value: '3', label: '其他' }
]

/** Market quotes with day change, filters, and a per-item daily close trend. */
export default function MarketPage() {
  const [items, setItems] = useState<MarketItemView[]>([])
  const [trends, setTrends] = useState<Record<string, ItemWithTrend[]>>({})
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(true)
  const [query, setQuery] = useState('')
  const [game, setGame] = useState('')
  const [type, setType] = useState('')

  const load = useCallback(() => {
    setBusy(true)
    fetchMarket(1, { q: query, game: game ? Number(game) : undefined, type: type ? Number(type) : undefined })
      .then((page) => { setItems(page.items); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
      .finally(() => setBusy(false))
  }, [query, game, type])

  useEffect(() => {
    load()
  }, [load])

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
      <View className="card">
        <Input
          className="search-input"
          placeholder="搜索名称或 classId"
          value={query}
          onInput={(e) => setQuery(e.detail.value)}
          onConfirm={load}
        />
        <View className="filter-row">
          {GAMES.map((option) => (
            <View
              key={option.value || 'all'}
              className={`filter-chip${game === option.value ? ' on' : ''}`}
              onClick={() => setGame(option.value)}
            >
              {option.label}
            </View>
          ))}
        </View>
        <View className="filter-row">
          {TYPES.map((option) => (
            <View
              key={option.value || 'all'}
              className={`filter-chip${type === option.value ? ' on' : ''}`}
              onClick={() => setType(option.value)}
            >
              {option.label}
            </View>
          ))}
        </View>
      </View>
      {error && !busy ? <ErrorBox text={error} onRetry={load} /> : null}
      {items.length === 0 && !error && busy ? <Skeleton cards={4} /> : null}
      {items.length === 0 && !error && !busy ? (
        <EmptyState title="暂无行情数据" hint="换个筛选条件，或等待行情同步" />
      ) : null}
      {items.map((item) => {
        // Day change comes from the API (two latest daily closes); red = up.
        const bps = item.changeBps
        return (
          <View key={item.id} className="card" onClick={() => loadTrend(item)}>
            <View className="row" style={{ justifyContent: 'space-between' }}>
              <Text style={{ flex: 1, fontWeight: 600 }} numberOfLines={1}>{item.marketHashName}</Text>
              <View style={{ textAlign: 'right' }}>
                <Text className="price">{item.priceCents != null ? `¥${yuan(item.priceCents)}` : '—'}</Text>
                <Text className={bps == null ? 'muted' : bps >= 0 ? 'change-up' : 'change-down'}>
                  {bps == null ? '—' : `${bps >= 0 ? '+' : ''}${(bps / 100).toFixed(2)}%`}
                </Text>
              </View>
            </View>
            <Text className="muted">{item.classId}</Text>
          </View>
        )
      })}
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
