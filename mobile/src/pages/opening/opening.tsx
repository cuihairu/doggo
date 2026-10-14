import { useCallback, useEffect, useState } from 'react'
import { Image, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchMyPrizes, fetchProfile, ApiError, getAccessToken, PrizeView, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, EmptyState, Msg, Muted, Price, Skeleton } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

/** Case quality codes as shown on the Steam market. */
const QUALITY_TEXT: Record<number, string> = {
  1: '消费级',
  2: '工业级',
  3: '军规级',
  4: '受限级',
  5: '保密级',
  6: '隐秘级',
  7: '稀有特殊'
}

/** Opening result card for the newest prize: item, rarity, reference price. */
function ResultCard({ prize }: { prize: PrizeView }) {
  const t = useTheme()
  const quality = QUALITY_TEXT[prize.quality] ?? (prize.rarity || '普通')
  return (
    <Card style={{ alignItems: 'center', borderColor: t.gold }}>
      <Muted>最新开箱结果</Muted>
      <View style={[styles.glow, { backgroundColor: t.gold }]}>
        {prize.iconUrl ? <Image source={{ uri: prize.iconUrl }} style={styles.glowIcon} /> : <Text style={{ color: '#fff', fontSize: 20 }}>?</Text>}
      </View>
      <Text numberOfLines={2} style={[styles.resultName, { color: t.text }]}>{prize.marketHashName}</Text>
      <View style={styles.tags}>
        <View style={[styles.tag, { backgroundColor: t.primary }]}>
          <Text style={styles.tagText}>{quality}</Text>
        </View>
        {prize.exterior ? (
          <View style={[styles.tag, { backgroundColor: t.border }]}>
            <Text style={[styles.tagText, { color: t.text }]}>{prize.exterior}</Text>
          </View>
        ) : null}
      </View>
      <Price>参考价 ¥{yuan(prize.marketPriceCents ?? prize.priceCents)}</Price>
      <Muted style={{ textAlign: 'center', marginTop: 6 }}>奖品已自动存入「我的奖品」</Muted>
      <Muted style={{ textAlign: 'center', marginTop: 2 }}>取回到 Steam 待 vapor 联调后开放</Muted>
    </Card>
  )
}

/** Opening results: the newest one as a result card, then the prizes inventory. */
export default function OpeningPage() {
  const t = useTheme()
  const [prizes, setPrizes] = useState<PrizeView[]>([])
  const [stale, setStale] = useState(false)
  const [error, setError] = useState('')
  const [needLogin, setNeedLogin] = useState(false)
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => {
    setLoading(true)
    setStale(false)
    setNeedLogin(false)
    if (!getAccessToken()) {
      setNeedLogin(true)
      setError('请先登录后查看开箱结果')
      setLoading(false)
      return
    }
    fetchProfile()
      .then((profile) => fetchMyPrizes(profile.id))
      .then((page) => {
        setPrizes(page.items)
        saveCache('doggo.cache.prizes', page.items)
        setError('')
      })
      .catch((e) => {
        if (e instanceof ApiError && e.status === 401) {
          setNeedLogin(true)
          setError('登录已过期，请重新登录')
          return
        }
        const cached = loadCache<PrizeView[]>('doggo.cache.prizes')
        if (cached?.length) {
          setPrizes(cached)
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
        {error ? <Msg text={error} onRetry={needLogin ? undefined : load} hint={stale ? '网络不佳，正在展示缓存内容' : undefined} /> : null}
        {needLogin ? (
          <Btn onPress={() => Taro.navigateTo({ url: '/pages/login/login' })}>去登录</Btn>
        ) : null}
        {prizes.length === 0 && !error && loading ? <Skeleton cards={3} /> : null}
        {prizes.length === 0 && !error && !loading && !needLogin ? (
          <EmptyState
            title="还没有奖品"
            hint="开箱或参与 Roll 房可获得奖品"
            actionLabel="去开一箱"
            onAction={() => Taro.navigateTo({ url: '/pages/products/products' })}
          />
        ) : null}
        {prizes.length > 0 ? <ResultCard prize={prizes[0]} /> : null}
        {prizes.map((prize) => (
          <Card key={prize.id}>
            <View style={styles.row}>
              {prize.iconUrl ? <Image source={{ uri: prize.iconUrl }} style={styles.icon} /> : null}
              <View style={{ flex: 1, marginHorizontal: 10 }}>
                <Text numberOfLines={1} style={{ color: t.text }}>{prize.marketHashName}</Text>
                <Muted>{prize.rarity || prize.exterior || ' '}</Muted>
              </View>
              <Price>¥{yuan(prize.marketPriceCents ?? prize.priceCents)}</Price>
            </View>
          </Card>
        ))}
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'center', minHeight: 56 },
  icon: { width: 44, height: 44, borderRadius: 6 },
  glow: { width: 72, height: 72, borderRadius: 36, alignItems: 'center', justifyContent: 'center', marginVertical: 10 },
  glowIcon: { width: 44, height: 44, borderRadius: 6 },
  resultName: { fontSize: 16, fontWeight: '700', textAlign: 'center' },
  tags: { flexDirection: 'row', gap: 6, marginVertical: 8 },
  tag: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 6 },
  tagText: { color: '#fff', fontSize: 12, fontWeight: '600' }
})
