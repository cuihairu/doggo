import { useEffect, useState } from 'react'
import { View, Text, ScrollView, Image } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchMyPrizes, fetchProfile, PrizeView } from '../../api/endpoints'
import { ApiError, getAccessToken, yuan } from '../../api/client'

/** Opening results: the prizes inventory, newest first. */
export default function OpeningPage() {
  const [prizes, setPrizes] = useState<PrizeView[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    if (!getAccessToken()) {
      setError('请先登录后查看开箱结果')
      return
    }
    fetchProfile()
      .then((profile) => fetchMyPrizes(profile.id))
      .then((page) => { setPrizes(page.items); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
  }, [])

  return (
    <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      {error && (
        <View className="card">
          <Text className="muted">{error}</Text>
          {!getAccessToken() && (
            <View className="btn-primary" style={{ marginTop: '16rpx' }} onClick={() => Taro.navigateTo({ url: '/pages/login/login' })}>
              去登录
            </View>
          )}
        </View>
      )}
      {prizes.length === 0 && !error && <View className="card"><Text className="muted">还没有奖品，去开一箱吧</Text></View>}
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
  )
}
