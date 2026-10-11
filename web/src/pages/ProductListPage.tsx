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

/** Store front: every on-shelf product links to its detail page. */
export default function ProductListPage() {
  const [result, setResult] = useState<ProductListResult | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api<ProductListResult>('/api/products?page=1&pageSize=24')
      .then(setResult)
      .catch((err) => setError(String(err.message ?? err)))
  }, [])

  return (
    <div>
      <h1 className="page-title">商店</h1>
      {error && <p className="error-text">{error}</p>}
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
