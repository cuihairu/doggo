import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

interface ProductListResult {
  total: number
  items: {
    id: string
    name: string
    iconUrl?: string
    status: number
    finalPriceCents: number
    regularPriceCents: number
  }[]
}

interface RecommendView {
  id: string
  productId: string
  productName: string
  productIconUrl: string
  title: string
  description: string
}

/** Store front: operator recommends on top, every on-shelf product links to its detail page. */
export default function ProductListPage() {
  const [result, setResult] = useState<ProductListResult | null>(null)
  const [recommends, setRecommends] = useState<RecommendView[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    api<ProductListResult>('/api/products?page=1&pageSize=24')
      .then(setResult)
      .catch((err) => setError(String(err.message ?? err)))
    api<RecommendView[]>('/api/recommends').then(setRecommends).catch(() => {})
  }, [])

  return (
    <div>
      <h1 className="page-title">商店</h1>
      {error && <p className="error-text">{error}</p>}
      {recommends.length > 0 && (
        <div style={{ display: 'flex', gap: 12, overflowX: 'auto', marginBottom: 16 }}>
          {recommends.map((recommend) => (
            <Link
              key={recommend.id}
              to={`/products/${recommend.productId}`}
              className="card"
              style={{ flex: '0 0 auto', width: 240, color: 'inherit', display: 'flex', gap: 10, alignItems: 'center' }}
            >
              {recommend.productIconUrl && (
                <img src={recommend.productIconUrl} alt="" style={{ width: 48, height: 48, objectFit: 'contain' }} />
              )}
              <span>
                <span style={{ fontSize: 14, display: 'block' }}>{recommend.title}</span>
                {recommend.description && (
                  <span className="muted" style={{ fontSize: 12, display: 'block' }}>{recommend.description}</span>
                )}
              </span>
            </Link>
          ))}
        </div>
      )}
      <div className="grid cols-4">
        {result?.items
          .filter((product) => product.status === 3)
          .map((product) => (
            <Link
              key={product.id}
              to={`/products/${product.id}`}
              className="card"
              style={{ color: 'inherit', textAlign: 'center' }}
            >
              {product.iconUrl && (
                <img src={product.iconUrl} alt="" style={{ width: 110, height: 110, objectFit: 'contain' }} />
              )}
              <div style={{ fontSize: 14, margin: '8px 0 4px' }}>{product.name}</div>
              <div style={{ color: 'var(--primary)', fontWeight: 600 }}>
                ¥{(product.finalPriceCents / 100).toFixed(2)}
              </div>
              {product.regularPriceCents > product.finalPriceCents && (
                <div className="muted" style={{ textDecoration: 'line-through' }}>
                  ¥{(product.regularPriceCents / 100).toFixed(2)}
                </div>
              )}
            </Link>
          ))}
      </div>
    </div>
  )
}
