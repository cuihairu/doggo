import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import NewsCard, { type NewsItem } from '../components/NewsCard'
import { EmptyState, ErrorBox, Skeleton } from '../components/States'

interface NewsListResult {
  total: number
  page: number
  pageSize: number
  items: NewsItem[]
}

interface NoticeView {
  id: string
  title: string
  content: string
}

interface BannerView {
  id: string
  title: string
  imageUrl: string
  linkUrl: string
}

/** Homepage: operator banners and notices on top of the news feed. */
export default function NewsListPage() {
  const [result, setResult] = useState<NewsListResult | null>(null)
  const [notices, setNotices] = useState<NoticeView[]>([])
  const [banners, setBanners] = useState<BannerView[]>([])
  const [error, setError] = useState('')
  const navigate = useNavigate()

  const load = useCallback(() => {
    api<NewsListResult>('/api/news?page=1&pageSize=12')
      .then(setResult)
      .catch((err) => setError(String(err.message ?? err)))
  }, [])

  useEffect(() => {
    load()
    api<NoticeView[]>('/api/notices').then(setNotices).catch(() => {})
    api<BannerView[]>('/api/banners').then(setBanners).catch(() => {})
  }, [load])

  function openBanner(link: string) {
    if (link.startsWith('/')) {
      navigate(link)
    } else if (link) {
      window.open(link, '_blank', 'noopener')
    }
  }

  return (
    <div>
      <h1 className="page-title">资讯</h1>
      {error && <ErrorBox detail={error} onRetry={load} />}
      {banners.length > 0 && (
        <div style={{ display: 'flex', gap: 12, overflowX: 'auto', marginBottom: 16 }}>
          {banners.map((banner) => (
            <div
              key={banner.id}
              onClick={() => openBanner(banner.linkUrl)}
              style={{
                flex: '0 0 auto',
                width: 280,
                cursor: banner.linkUrl ? 'pointer' : 'default',
                borderRadius: 'var(--radius)',
                overflow: 'hidden',
                border: '1px solid var(--border)',
              }}
            >
              <img src={banner.imageUrl} alt={banner.title} style={{ width: '100%', height: 110, objectFit: 'cover', display: 'block' }} />
              <p style={{ margin: '8px 10px', fontSize: 13 }}>{banner.title}</p>
            </div>
          ))}
        </div>
      )}
      {notices.length > 0 && (
        <div className="card" style={{ marginBottom: 16 }}>
          {notices.map((notice, index) => (
            <p key={notice.id} className="muted" style={{ margin: index > 0 ? '8px 0 0' : 0 }}>
              <strong style={{ color: 'var(--primary)' }}>公告</strong> {notice.title}：{notice.content}
            </p>
          ))}
        </div>
      )}
      {!result && !error && <Skeleton lines={6} />}
      <div className="grid cols-3">
        {result?.items.map((item) => <NewsCard key={item.id} item={item} />)}
      </div>
      {result && result.items.length === 0 && <EmptyState title="暂无资讯" hint="资讯发布后会出现在这里" />}
    </div>
  )
}
