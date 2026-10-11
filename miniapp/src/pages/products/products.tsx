import { useEffect, useState } from 'react'
import { View, Text, ScrollView, Image } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchProducts, ProductView } from '../../api/endpoints'
import { ApiError, yuan } from '../../api/client'

/** On-shelf products grid; tap a crate to open its plan list. */
export default function ProductsPage() {
  const [items, setItems] = useState<ProductView[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    fetchProducts(1)
      .then((page) => { setItems(page.items.filter((p) => p.status === 3)); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
  }, [])

  return (
    <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      {error && <View className="card"><Text className="muted">{error}</Text></View>}
      {items.length === 0 && <View className="card"><Text className="muted">加载中…</Text></View>}
      {items.map((item) => (
        <View
          key={item.id}
          className="card"
          onClick={() => Taro.navigateTo({ url: `/pages/product/product?id=${item.id}` })}
        >
          {item.iconUrl && <Image src={item.iconUrl} style={{ width: '72rpx', height: '72rpx' }} mode="aspectFill" />}
          <Text style={{ fontWeight: 600 }}>{item.name}</Text>
          <Text className="muted">
            {yuan(item.finalPriceCents)} 起
            {item.regularPriceCents > item.finalPriceCents ? ` · 原价 ${yuan(item.regularPriceCents)}` : ''}
          </Text>
        </View>
      ))}
    </ScrollView>
  )
}
