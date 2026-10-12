import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api/client'

interface ArticleView {
  id: string
  title: string
  summary?: string | null
  contentHtml: string
  viewCount: number
  createdAtUtc: string
}

/** Operator-authored article (banner links and campaigns point here). */
export default function ArticlePage() {
  const { id } = useParams()
  const [article, setArticle] = useState<ArticleView | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    api<ArticleView>(`/api/articles/${id}`)
      .then(setArticle)
      .catch((err) => setError(String(err.message ?? err)))
  }, [id])

  if (error) return <p className="error-text">{error}</p>
  if (!article) return <p className="muted">加载中…</p>

  return (
    <div>
      <h1 className="page-title">{article.title}</h1>
      <p className="muted">
        {article.createdAtUtc.slice(0, 10)} · {article.viewCount} 次浏览
      </p>
      {article.summary && <p className="muted">{article.summary}</p>}
      <div className="card" dangerouslySetInnerHTML={{ __html: article.contentHtml }} />
    </div>
  )
}
