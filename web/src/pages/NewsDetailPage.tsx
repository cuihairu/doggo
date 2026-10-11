import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api/client'

interface NewsDetail {
  meta: {
    id: string
    title: string
    summary?: string
    publishedAtUtc?: string
  }
  author?: string | null
  contentHtml?: string | null
}

export default function NewsDetailPage() {
  const { id } = useParams()
  const [detail, setDetail] = useState<NewsDetail | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    api<NewsDetail>(`/api/news/${id}`)
      .then(setDetail)
      .catch((err) => setError(String(err.message ?? err)))
  }, [id])

  if (error) return <p className="error-text">{error}</p>
  if (!detail) return <p className="muted">加载中…</p>

  return (
    <article className="card">
      <h1 className="page-title">{detail.meta.title}</h1>
      <p className="muted">
        {detail.author && <span>作者 {detail.author} · </span>}
        {detail.meta.publishedAtUtc}
      </p>
      {detail.contentHtml ? (
        <div dangerouslySetInnerHTML={{ __html: detail.contentHtml }} />
      ) : (
        <p>{detail.meta.summary}</p>
      )}
    </article>
  )
}
