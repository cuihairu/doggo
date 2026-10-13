import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Image } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchMyPrizes, fetchProfile, PrizeView } from '../../api/endpoints'
import { ApiError, getAccessToken, yuan } from '../../api/client'
import ThemeRoot from '../../ThemeRoot'
import { EmptyState, ErrorBox, Skeleton } from '../../states'

/** Opening results: the prizes inventory, newest first. */
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
