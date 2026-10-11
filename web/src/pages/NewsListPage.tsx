import { useEffect, useState } from 'react'
import { api } from '../api/client'
import NewsCard, { type NewsItem } from '../components/NewsCard'

interface NewsListResult {
  total: number
  page: number
  pageSize: number
  items: NewsItem[]
}

export default function NewsListPage() {
  const [result, setResult] = useState<NewsListResult | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api<NewsListResult>('/api/news?page=1&pageSize=12')
      .then(setResult)
      .catch((err) => setError(String(err.message ?? err)))
  }, [])

  return (
    <div>
      <h1 className="page-title">资讯</h1>
      {error && <p className="error-text">{error}</p>}
      <div className="grid cols-3">
        {result?.items.map((item) => <NewsCard key={item.id} item={item} />)}
      </div>
      {result && result.items.length === 0 && <p className="muted">暂无资讯</p>}
    </div>
  )
}
