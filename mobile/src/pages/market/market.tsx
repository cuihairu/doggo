import { useCallback, useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, TextInput, View } from 'react-native'
import Svg, { Path } from 'react-native-svg'
import Taro from '@tarojs/taro'
import { fetchMarket, fetchMarketPrices, ApiError, MarketItemView, PricePoint, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, EmptyState, Msg, Muted, Price, Skeleton } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

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
  const [query, setQuery] = useState('')
  const [game, setGame] = useState('')
  const [type, setType] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setStale(false)
    fetchMarket(1, { q: query, game: game ? Number(game) : undefined, type: type ? Number(type) : undefined })
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
  }, [query, game, type])

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
        <Card>
          <TextInput
            placeholder="搜索名称或 classId"
            placeholderTextColor={t.muted}
            value={query}
            onChangeText={setQuery}
            onSubmitEditing={load}
            autoCapitalize="none"
            style={[styles.search, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          <View style={styles.filters}>
            {GAMES.map((option) => (
              <Btn
                key={option.value || 'all'}
                tone={game === option.value ? undefined : 'plain'}
                style={styles.filterBtn}
                onPress={() => setGame(option.value)}
              >
                {option.label}
              </Btn>
            ))}
          </View>
          <View style={styles.filters}>
            {TYPES.map((option) => (
              <Btn
                key={option.value || 'all'}
                tone={type === option.value ? undefined : 'plain'}
                style={styles.filterBtn}
                onPress={() => setType(option.value)}
              >
                {option.label}
              </Btn>
            ))}
          </View>
        </Card>
        {stale ? <Msg text="网络不佳，正在展示缓存内容" /> : null}
        {error && !stale ? <Msg text={error} onRetry={load} /> : null}
        {items.length === 0 && !error && loading ? <Skeleton cards={4} /> : null}
        {items.length === 0 && !error && !loading ? (
          <EmptyState title="暂无行情数据" hint="换个筛选条件，或等待行情同步" />
        ) : null}
        {items.map((item) => {
          // Day change comes from the API (two latest daily closes); red = up.
          const bps = item.changeBps
          return (
            <Card key={item.id}>
              <View
                style={styles.touchRow}
                onStartShouldSetResponder={() => true}
                onResponderRelease={() => loadTrend(item)}
              >
                <Text numberOfLines={1} style={[styles.name, { color: t.text, flex: 1 }]}>{item.marketHashName}</Text>
                <View style={{ alignItems: 'flex-end' }}>
                  <Price>{item.priceCents != null ? `¥${yuan(item.priceCents)}` : '—'}</Price>
                  <Text style={{ color: bps == null ? t.muted : bps >= 0 ? t.danger : t.success, fontSize: 12 }}>
                    {bps == null ? '—' : `${bps >= 0 ? '+' : ''}${(bps / 100).toFixed(2)}%`}
                  </Text>
                </View>
              </View>
              <Muted>{item.classId}</Muted>
            </Card>
          )
        })}
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
  section: { fontSize: 16, fontWeight: '700', marginBottom: 6 },
  search: { minHeight: 44, borderWidth: 1, borderRadius: 8, paddingHorizontal: 12 },
  filters: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: 8 },
  filterBtn: { paddingHorizontal: 12, paddingVertical: 6, minHeight: 36 }
})
