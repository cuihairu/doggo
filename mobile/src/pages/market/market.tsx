import { useCallback, useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, View } from 'react-native'
import Svg, { Path } from 'react-native-svg'
import Taro from '@tarojs/taro'
import { fetchMarket, fetchMarketPrices, ApiError, MarketItemView, PricePoint, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

interface ItemWithTrend {
  item: MarketItemView
  points: PricePoint[]
}

/** 30-day close trend as a plain SVG polyline — same math as the web
 * MarketDetailPage, no chart library (web MarketDetailPage parity). */
function TrendLine({ points, color }: { points: PricePoint[]; color: string }) {
  const chronological = points.slice().reverse() // the API returns newest first
  if (chronological.length < 2) return null
  const width = 320
  const height = 110
  const closes = chronological.map((p) => p.closeCents)
  const min = Math.min(...closes, 0)
  const max = Math.max(...closes, 1)
  const stepX = width / (chronological.length - 1)
  const d = chronological
    .map((p, i) => {
      const x = i * stepX
      const y = height - ((p.closeCents - min) / (max - min || 1)) * (height - 16) - 8
      return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
  return (
    <Svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none">
      <Path d={d} fill="none" stroke={color} strokeWidth={2} />
    </Svg>
  )
}

/** Market quotes with a per-item daily close trend. */
export default function MarketPage() {
  const t = useTheme()
  const [items, setItems] = useState<MarketItemView[]>([])
  const [trends, setTrends] = useState<Record<string, ItemWithTrend[]>>({})
  const [stale, setStale] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => {
    setLoading(true)
    setStale(false)
    fetchMarket(1)
      .then((page) => {
        setItems(page.items)
        saveCache('doggo.cache.marketAll', page.items)
        setError('')
      })
      .catch((e) => {
        const cached = loadCache<MarketItemView[]>('doggo.cache.marketAll')
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

  function change(points: PricePoint[]): { latest: number; pct: number | null } {
    if (points.length === 0) return { latest: 0, pct: null }
    // The API returns newest first.
    const latest = points[0].closeCents
    const base = points[points.length - 1]?.closeCents ?? 0
    const pct = points.length < 2 || base <= 0
      ? null
      : ((latest - base) / base) * 100
    return { latest, pct }
  }

  async function loadTrend(item: MarketItemView) {
    const result = await fetchMarketPrices(item.id).catch(() => null)
    if (!result) return
    setTrends((prev) => ({ ...prev, [item.id]: [{ item, points: result.points }] }))
  }

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
              onResponderRelease={() => loadTrend(item)}
            >
              <Text numberOfLines={1} style={[styles.name, { color: t.text, flex: 1 }]}>{item.marketHashName}</Text>
              <Price>{item.priceCents != null ? `¥${yuan(item.priceCents)}` : '—'}</Price>
            </View>
            <Muted>{item.classId}</Muted>
          </Card>
        ))}
        <Card>
          <Text style={[styles.section, { color: t.text }]}>近 30 日收盘</Text>
          {Object.entries(trends).map(([id, rows]) => {
            const { latest, pct } = change(rows[0].points)
            return (
              <View key={id} style={{ marginTop: 10 }}>
                <Text style={{ color: t.text }}>{rows[0].item.marketHashName}</Text>
                {rows[0].points.length >= 2 ? <TrendLine points={rows[0].points} color={t.primary} /> : null}
                <Muted>
                  {latest > 0 ? `最新 ¥${yuan(latest)}${pct != null ? ` · 期间 ${pct.toFixed(1)}%` : ''}` : '暂无数据'}
                </Muted>
              </View>
            )
          })}
          {Object.keys(trends).length === 0 && <Muted>点击上方行情查看走势</Muted>}
        </Card>
        <Btn tone="plain" onPress={load}>刷新</Btn>
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  touchRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: 44 },
  name: { fontSize: 15, fontWeight: '600' },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 6 }
})
