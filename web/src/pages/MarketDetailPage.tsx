import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api/client'
import { EmptyState, ErrorBox, Skeleton } from '../components/States'

interface PriceHistory {
  item: string
  points: {
    date: string
    openCents: number
    highCents: number
    lowCents: number
    closeCents: number
  }[]
}

/** Renders close prices as a simple inline SVG line — no chart library needed. */
export default function MarketDetailPage() {
  const { id } = useParams()
  const [history, setHistory] = useState<PriceHistory | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    if (!id) return
    api<PriceHistory>(`/api/market/items/${id}/prices`)
      .then(setHistory)
      .catch((err) => setError(String(err.message ?? err)))
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  if (error) return <ErrorBox detail={error} onRetry={load} />
  if (!history) return <Skeleton lines={5} />

  const points = history.points
  const width = 720
  const height = 220
  const closes = points.map((point) => point.closeCents)
  const min = Math.min(...closes, 0)
  const max = Math.max(...closes, 1)
  const stepX = points.length > 1 ? width / (points.length - 1) : width
  const toPath = () =>
    points
      .map((point, index) => {
        const x = index * stepX
        const y = height - ((point.closeCents - min) / (max - min || 1)) * (height - 20) - 10
        return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
      })
      .join(' ')

  return (
    <div>
      <h1 className="page-title">价格趋势</h1>
      <div className="card">
        {points.length === 0 ? (
          <EmptyState title="暂无价格数据" hint="行情同步后会生成价格序列" />
        ) : (
          <>
            <svg width="100%" viewBox={`0 0 ${width} ${height}`} role="img" aria-label="price trend">
              <path d={toPath()} fill="none" stroke="var(--primary)" strokeWidth="2" />
            </svg>
            <table className="table">
              <thead>
                <tr>
                  <th>日期</th>
                  <th>开</th>
                  <th>高</th>
                  <th>低</th>
                  <th>收</th>
                </tr>
              </thead>
              <tbody>
                {points.map((point) => (
                  <tr key={point.date}>
                    <td>{point.date}</td>
                    <td>{(point.openCents / 100).toFixed(2)}</td>
                    <td className="price-up">{(point.highCents / 100).toFixed(2)}</td>
                    <td className="price-down">{(point.lowCents / 100).toFixed(2)}</td>
                    <td>{(point.closeCents / 100).toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>
    </div>
  )
}
