import { useCallback, useEffect, useState } from 'react'
import { Image, ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchMyPrizes, fetchProfile, ApiError, getAccessToken, PrizeView, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted, Price } from '../../ui'
import { useTheme } from '../../theme'
import { loadCache, saveCache } from '../../cache'

/** Opening results: the prizes inventory, newest first. */
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
        {prizes.length === 0 && !error && loading ? <Msg text="加载中…" /> : null}
        {prizes.length === 0 && !error && !loading ? <Msg text="还没有奖品，去开一箱吧" /> : null}
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
  icon: { width: 44, height: 44, borderRadius: 6 }
})
