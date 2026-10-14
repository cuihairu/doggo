import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Image } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchMyPrizes, fetchProfile, PrizeView } from '../../api/endpoints'
import { ApiError, getAccessToken, yuan } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'
import { EmptyState, ErrorBox, Skeleton } from '../../states'

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
  const quality = QUALITY_TEXT[prize.quality] ?? (prize.rarity || '普通')
  return (
    <View className="card result-card">
      <View className="result-box">
        <Text className="muted">最新开箱结果</Text>
        <View className="result-glow">
          {prize.iconUrl
            ? <Image src={prize.iconUrl} style={{ width: '88rpx', height: '88rpx' }} mode="aspectFit" />
            : <Text style={{ color: '#fff', fontSize: '40rpx' }}>?</Text>}
        </View>
        <Text className="result-name">{prize.marketHashName}</Text>
        <View className="row" style={{ gap: '12rpx', margin: '8rpx 0' }}>
          <Text className="rarity-tag">{quality}</Text>
          {prize.exterior ? <Text className="rarity-tag plain">{prize.exterior}</Text> : null}
        </View>
        <Text className="price">参考价 ¥{yuan(prize.marketPriceCents ?? prize.priceCents)}</Text>
        <Text className="muted" style={{ marginTop: '8rpx' }}>奖品已自动存入「我的奖品」</Text>
        <Text className="muted">取回到 Steam 待 vapor 联调后开放</Text>
      </View>
    </View>
  )
}

/** Opening results: the newest one as a result card, then the prizes inventory. */
export default function OpeningPage() {
  const [prizes, setPrizes] = useState<PrizeView[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(true)

  const load = useCallback(() => {
    setBusy(true)
    if (!getAccessToken()) {
      setError('请先登录后查看开箱结果')
      setBusy(false)
      return
    }
    fetchProfile()
      .then((profile) => fetchMyPrizes(profile.id))
      .then((page) => { setPrizes(page.items); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
      .finally(() => setBusy(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  return (
    <ThemeRoot>
      <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      {error ? (
        <ErrorBox text={error} onRetry={getAccessToken() ? load : undefined}>
          {!getAccessToken() && (
            <View className="btn-primary" style={{ marginTop: '16rpx' }} onClick={() => Taro.navigateTo({ url: '/pages/login/login' })}>
              去登录
            </View>
          )}
        </ErrorBox>
      ) : null}
      {prizes.length === 0 && !error && busy ? <Skeleton cards={3} /> : null}
      {prizes.length === 0 && !error && !busy && getAccessToken() ? (
        <EmptyState
          title="还没有奖品"
          hint="开箱或参与 Roll 房可获得奖品"
          actionLabel="去开一箱"
          onAction={() => Taro.navigateTo({ url: '/pages/products/products' })}
        />
      ) : null}
      {prizes.length > 0 ? <ResultCard prize={prizes[0]} /> : null}
      {prizes.map((prize) => (
        <View key={prize.id} className="card row">
          {prize.iconUrl && <Image src={prize.iconUrl} style={{ width: '88rpx', height: '88rpx' }} mode="aspectFill" />}
          <View style={{ flex: 1, marginLeft: '16rpx' }}>
            <Text numberOfLines={1}>{prize.marketHashName}</Text>
            <Text className="muted">{prize.rarity || prize.exterior || ' '}</Text>
          </View>
          <Text className="price">¥{yuan(prize.marketPriceCents ?? prize.priceCents)}</Text>
        </View>
      ))}
      </ScrollView>
    </ThemeRoot>
  )
}
