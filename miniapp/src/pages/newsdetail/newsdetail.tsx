import { useEffect, useState } from 'react'
import { View, Text, RichText, ScrollView } from '@tarojs/components'
import Taro from '@tarojs/taro'

interface NewsDetail {
  meta: { title: string; source: string; category: string; publishedAtUtc: string; summary?: string | null }
  author?: string | null
  contentHtml: string
}

/** News article: meta plus the scraped body rendered as rich text. */
export default function NewsDetailPage() {
  const [detail, setDetail] = useState<NewsDetail | null>(null)

  useEffect(() => {
    const params = Taro.getCurrentInstance().router?.params ?? {}
    if (!params.id) return
    Taro.request({ url: `${process.env.TARO_APP_API_BASE ?? ''}/api/news/${params.id}` })
      .then((res) => setDetail(res.data as NewsDetail))
      .catch(() => undefined)
  }, [])

  if (!detail) {
    return <View className="card"><Text className="muted">加载中…</Text></View>
  }

  const { meta, author, contentHtml } = detail
  return (
    <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      <View className="card">
        <Text style={{ fontSize: '34rpx', fontWeight: 700 }}>{meta.title}</Text>
        <View className="row" style={{ justifyContent: 'space-between', marginTop: '12rpx' }}>
          <Text className="muted">{meta.source} · {meta.category}</Text>
          <Text className="muted">{String(meta.publishedAtUtc ?? '').slice(0, 19).replace('T', ' ')}</Text>
        </View>
        {author && <Text className="muted">作者:{author}</Text>}
      </View>
      <RichText className="card" nodes={contentHtml} />
    </ScrollView>
  )
}
