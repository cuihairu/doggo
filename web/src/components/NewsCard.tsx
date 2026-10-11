import { Link } from 'react-router-dom'

export interface NewsItem {
  id: string
  title: string
  summary?: string
  coverUrl?: string | null
  category?: string
  publishedAtUtc?: string
}

/** Compact card for one news entry, linking to the detail page. */
export default function NewsCard({ item }: { item: NewsItem }) {
  return (
    <Link to={`/news/${item.id}`} className="card" style={{ display: 'block', color: 'inherit' }}>
      {item.coverUrl && (
        <img
          src={item.coverUrl}
          alt=""
          style={{ width: '100%', height: 130, objectFit: 'cover', borderRadius: 'var(--radius)' }}
        />
      )}
      <h3 style={{ margin: '8px 0 6px', fontSize: 15, lineHeight: 1.4 }}>{item.title}</h3>
      {item.summary && <p className="muted" style={{ margin: 0 }}>{item.summary}</p>}
    </Link>
  )
}
