import { useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, View } from 'react-native'
import Taro, { useRouter } from '@tarojs/taro'
import { api, ApiError } from '@doggo/shared'
import { SafeTop, Card, Msg, Muted, Skeleton } from '../../ui'
import { useTheme } from '../../theme'

interface NewsDetail {
  meta: { title: string; source: string; category: string; publishedAtUtc: string; summary?: string | null }
  author?: string | null
  contentHtml: string
}

/** RN has no RichText component — render the scraped body as plain text. */
function htmlToText(html: string): string {
  return html
    .replace(/<style[\s\S]*?<\/style>/gi, '')
    .replace(/<script[\s\S]*?<\/script>/gi, '')
    .replace(/<\/(p|div|h[1-6]|li|tr)>/gi, '\n')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
}

export default function NewsDetailPage() {
  const t = useTheme()
  const router = useRouter()
  const [detail, setDetail] = useState<NewsDetail | null>(null)
  const [error, setError] = useState('')
  const [body, setBody] = useState('')

  async function load(id?: string) {
    if (!id) return
    setError('')
    try {
      const result = await api<NewsDetail>(`/api/news/${id}`)
      setDetail(result)
      setBody(htmlToText(result.contentHtml ?? ''))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '加载失败')
    }
  }

  useEffect(() => {
    const params = (Taro.getCurrentInstance().router?.params ?? {}) as { id?: string }
    load(params.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router.params])

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        {error ? <Msg text={error} onRetry={() => load((router.params ?? {})?.id)} /> : null}
        {!detail && !error ? <Skeleton cards={3} /> : null}
        {detail && (
          <>
            <Card>
              <Text style={[styles.title, { color: t.text }]}>{detail.meta.title}</Text>
              <View style={styles.row}>
                <Muted>{detail.meta.source} · {detail.meta.category}</Muted>
                <Muted>{String(detail.meta.publishedAtUtc ?? '').slice(0, 19).replace('T', ' ')}</Muted>
              </View>
              {detail.author ? <Muted>作者:{detail.author}</Muted> : null}
            </Card>
            <Card>
              <Text style={{ color: t.text, lineHeight: 22 }}>{body}</Text>
            </Card>
          </>
        )}
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  title: { fontSize: 19, fontWeight: '700' },
  row: { flexDirection: 'row', justifyContent: 'space-between', marginTop: 10 }
})
