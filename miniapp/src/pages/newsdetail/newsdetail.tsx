import { useCallback, useEffect, useState } from 'react'
import { View, Text, RichText, ScrollView } from '@tarojs/components'
import Taro from '@tarojs/taro'
import filterXSS, { IFilterXSSOptions } from 'xss'
import ThemeRoot from '../../ThemeRoot'
import { ErrorBox, Skeleton } from '../../states'

interface NewsDetail {
  meta: { title: string; source: string; category: string; publishedAtUtc: string; summary?: string | null }
  author?: string | null
  contentHtml: string
}

/** Client-side whitelist hardening for the scraped body. The server already
 * sanitizes at ingest (NewsStore), this second pass mirrors the web client's
 * DOMPurify as depth-in-depth for the miniapp RichText node. */
const RICH_TEXT_XSS_OPTIONS: IFilterXSSOptions = {
  stripIgnoreTagBody: ['script', 'style', 'iframe', 'svg', 'math', 'object', 'embed', 'video', 'audio', 'form', 'input', 'button']
}

/** News article: meta plus the scraped body rendered as rich text. */
export default function NewsDetailPage() {
  const [detail, setDetail] = useState<NewsDetail | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    const params = Taro.getCurrentInstance().router?.params ?? {}
    if (!params.id) return
    Taro.request({ url: `${process.env.TARO_APP_API_BASE ?? ''}/api/news/${params.id}` })
      .then((res) => setDetail(res.data as NewsDetail))
      .catch(() => setError('加载失败，请重试'))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  if (error) {
    return (
      <ThemeRoot>
        <ErrorBox text={error} onRetry={load} />
      </ThemeRoot>
    )
  }
  if (!detail) {
    return (
      <ThemeRoot>
        <Skeleton cards={2} />
      </ThemeRoot>
    )
  }

  const { meta, author, contentHtml } = detail
  return (
    <ThemeRoot>
      <ScrollView scrollY enableFlex style={{ height: '100vh' }}>
      <View className="card">
        <Text style={{ fontSize: '34rpx', fontWeight: 700 }}>{meta.title}</Text>
        <View className="row" style={{ justifyContent: 'space-between', marginTop: '12rpx' }}>
          <Text className="muted">{meta.source} · {meta.category}</Text>
          <Text className="muted">{String(meta.publishedAtUtc ?? '').slice(0, 19).replace('T', ' ')}</Text>
        </View>
        {author && <Text className="muted">作者:{author}</Text>}
      </View>
      <RichText className="card" nodes={filterXSS(contentHtml, RICH_TEXT_XSS_OPTIONS)} />
      </ScrollView>
    </ThemeRoot>
  )
}
