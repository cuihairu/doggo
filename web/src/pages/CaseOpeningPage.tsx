import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'

interface SeatView {
  seatNo: number
  status: number
  crateAssetId?: string | null
  keyAssetId?: string | null
  prizeAssetId?: string | null
  orderId?: string | null
}

interface BatchView {
  id: string
  productId: string
  lot: string
  seatCount: number
  soldCount: number
  status: number
  seats: SeatView[]
}

const STATUS_TEXT: Record<number, string> = {
  0: '待领取',
  1: '待支付',
  2: '已开奖',
  3: '已完成',
}

/** Group-buying case opening board: every seat with its live state. */
export default function CaseOpeningPage() {
  const { batchId } = useParams()
  const [batch, setBatch] = useState<BatchView | null>(null)
  const [error, setError] = useState('')

  const reload = useCallback(() => {
    if (!batchId) return
    api<BatchView>(`/api/group-buyings/${batchId}`)
      .then(setBatch)
      .catch((err) => setError(String(err.message ?? err)))
  }, [batchId])

  useEffect(() => {
    reload()
    const timer = setInterval(reload, 5000)
    return () => clearInterval(timer)
  }, [reload])

  if (!batch) return <p className="muted">{error || '加载中…'}</p>

  return (
    <div>
      <h1 className="page-title">开箱拼团 · {batch.lot}</h1>
      <p className="muted">
        {batch.soldCount}/{batch.seatCount} 座位已售
      </p>
      {error && <p className="error-text">{error}</p>}
      <div className="grid cols-4">
        {batch.seats.map((seat) => (
          <div key={seat.seatNo} className="card" style={{ textAlign: 'center' }}>
            <div className="muted">座位 {seat.seatNo}</div>
            <div style={{ margin: '10px 0' }}>{STATUS_TEXT[seat.status] ?? seat.status}</div>
            {seat.prizeAssetId && (
              <div className="rarity-tag">已出货</div>
            )}
          </div>
        ))}
      </div>
      <p style={{ marginTop: 16 }}>
        <Link to="/prizes">查看我的奖品 →</Link>
      </p>
    </div>
  )
}
