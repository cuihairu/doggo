import { useCallback, useEffect, useState } from 'react'
import { View, Text, ScrollView, Image, Input, Button } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { fetchProducts, ProductView } from '../../api/endpoints'
import { ApiError, yuan } from '../../api/client'

const PAGE_SIZE = 20

/** On-shelf products list with search and load-more; tap a crate to open its plan list. */
export default function ProductsPage() {
  const [items, setItems] = useState<ProductView[]>([])
  const [total, setTotal] = useState(0)
  const [loaded, setLoaded] = useState(0)
  const [searchInput, setSearchInput] = useState('')
  const [q, setQ] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const loadPage = useCallback((page: number, append: boolean, query: string) => {
    setBusy(true)
    fetchProducts(page, { q: query })
      .then((result) => {
        const shelf = result.items.filter((p) => p.status === 3)
        setItems((prev) => (append ? [...prev, ...shelf] : shelf))
        setTotal(result.total)
        setLoaded((prev) => (append ? prev + shelf.length : shelf.length))
        setError('')
      })
      .catch((e) => setError(e instanceof ApiError ? e.message : '加载失败'))
      .finally(() => setBusy(false))
  }, [])

  // Search changes restart from page 1.
  useEffect(() => {
    loadPage(1, false, q)
  }, [loadPage, q])

  return (
    <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      <View className="card">
        <View className="row">
          <Input
            placeholder="搜索商品名称"
            value={searchInput}
            onInput={(e) => setSearchInput(e.detail.value)}
            confirmType="search"
            onConfirm={() => setQ(searchInput.trim())}
            style={{ border: '1rpx solid #eee', borderRadius: '8rpx', padding: '12rpx', flex: 1, marginRight: '12rpx' }}
          />
          <Button size="mini" className="btn-primary" onClick={() => setQ(searchInput.trim())}>
            搜索
          </Button>
        </View>
      </View>
      {error && <View className="card"><Text className="muted">{error}</Text></View>}
      {items.length === 0 && !error && !busy && (
        <View className="card"><Text className="muted">没有符合条件的商品</Text></View>
      )}
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
      {items.length > 0 && loaded < total && (
        <Button size="mini" className="btn-primary" disabled={busy} onClick={() => loadPage(Math.floor(loaded / PAGE_SIZE) + 1, true, q)}>
          {busy ? '加载中…' : `加载更多（${loaded}/${total}）`}
        </Button>
      )}
    </ScrollView>
  )
}
