import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

interface MarketListResult {
  total: number
  items: {
    id: string
    marketHashName: string
    latestPriceCents: number
    currency: string
    priceUpdatedAtUtc?: string
  }[]
}

export default function MarketPage() {
  const [result, setResult] = useState<MarketListResult | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api<MarketListResult>('/api/market/items?page=1&pageSize=50')
      .then(setResult)
      .catch((err) => setError(String(err.message ?? err)))
  }, [])

  return (
    <div>
      <h1 className="page-title">行情</h1>
      {error && <p className="error-text">{error}</p>}
      <div className="card">
        <table className="table">
          <thead>
            <tr>
              <th>饰品</th>
              <th>最新价</th>
              <th>更新时间</th>
            </tr>
          </thead>
          <tbody>
            {result?.items.map((item) => (
              <tr key={item.id}>
                <td>
                  <Link to={`/market/${item.id}`}>{item.marketHashName}</Link>
                </td>
                <td>
                  {(item.latestPriceCents / 100).toFixed(2)} {item.currency}
                </td>
                <td className="muted">{item.priceUpdatedAtUtc?.slice(0, 19).replace('T', ' ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
