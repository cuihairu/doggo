import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorBox, Skeleton } from '../components/States'

interface PrizeView {
  assetId: string
  classId: string
  instanceId: string
  marketHashName: string
  quality: number
  exterior: string
  floatValue?: number | null
  rarity: string
  iconUrl: string
  inspectLink?: string | null
  priceCents: number
  tradableAtUtc?: string | null
}

interface SeatView {
  seatNo: number
  status: number
  crateAssetId?: string | null
  keyAssetId?: string | null
  prizeAssetId?: string | null
  orderId?: string | null
  prize?: PrizeView | null
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

const BATCH_STATUS_TEXT: Record<number, string> = {
  0: '开售中',
  1: '售罄',
  2: '已结束',
}

const SEAT_STATUS_TEXT: Record<number, string> = {
  0: '待领取',
  1: '已锁定',
  2: '已支付',
  3: '开箱中',
  4: '已出货',
  5: '取库存中',
  6: '待交易',
  7: '交易中',
  8: '已完成',
}

/** Case quality codes as shown on the Steam market. */
const QUALITY_TEXT: Record<number, string> = {
  1: '消费级',
  2: '工业级',
  3: '军规级',
  4: '受限级',
  5: '保密级',
  6: '隐秘级',
  7: '稀有特殊',
}

/** Result card for a seat whose opening result has landed. */
function OpeningResultCard({ seat }: { seat: SeatView }) {
  const prize = seat.prize
  if (!prize) {
    return (
      <div className="card" style={{ textAlign: 'center' }}>
        <div className="muted">座位 {seat.seatNo}</div>
        <div style={{ margin: '10px 0' }}>{SEAT_STATUS_TEXT[seat.status] ?? seat.status}</div>
        {seat.prizeAssetId && <span className="rarity-tag">已出货</span>}
      </div>
    )
  }

  const quality = QUALITY_TEXT[prize.quality] ?? (prize.rarity || '普通')
  return (
    <div className="card" style={{ textAlign: 'center', borderColor: 'var(--primary)' }}>
      <div className="open-glow">
        {prize.iconUrl ? (
          <img src={prize.iconUrl} alt="" style={{ width: 44, height: 44, objectFit: 'contain' }} />
        ) : (
          <span style={{ fontSize: 20 }}>?</span>
        )}
      </div>
      <div style={{ fontWeight: 600, margin: '8px 0 4px' }}>{prize.marketHashName}</div>
      <div style={{ display: 'flex', gap: 6, justifyContent: 'center', flexWrap: 'wrap', marginBottom: 8 }}>
        <span className="rarity-tag">{quality}</span>
        {prize.exterior && <span className="rarity-tag">{prize.exterior}</span>}
      </div>
      {prize.floatValue != null && <div className="muted" style={{ fontSize: 12 }}>磨损 {prize.floatValue.toFixed(4)}</div>}
      <div className="muted" style={{ marginTop: 4 }}>参考价 ¥{(prize.priceCents / 100).toFixed(2)}</div>
      {prize.tradableAtUtc && (
        <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
          {new Date(prize.tradableAtUtc).toLocaleDateString('zh-CN')} 起可交易
        </div>
      )}
      <div className="muted" style={{ fontSize: 12, marginTop: 10 }}>奖品已自动存入「我的奖品」</div>
      <div style={{ marginTop: 8 }}>
        {prize.inspectLink && (
          <a className="btn secondary sm" href={prize.inspectLink} target="_blank" rel="noreferrer">
            检视链接
          </a>
        )}
      </div>
    </div>
  )
}

/** Group-buying case opening board: every seat with its live state and result. */
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

  if (!batch) {
    return error ? <ErrorBox detail={error} onRetry={reload} /> : <Skeleton lines={5} />
  }

  return (
    <div>
      <h1 className="page-title">开箱拼团 · {batch.lot}</h1>
      <p className="muted">
        {batch.soldCount}/{batch.seatCount} 座位已售 · {BATCH_STATUS_TEXT[batch.status] ?? batch.status}
      </p>
      <p className="muted" style={{ fontSize: 12, marginTop: -6 }}>
        座位状态由开箱回调写入；真实开箱与取回到 Steam 依赖 vapor，联调前结果卡只读。
      </p>
      {error && <p className="error-text">{error}</p>}
      <div className="grid cols-4">
        {batch.seats.map((seat) => (
          <OpeningResultCard key={seat.seatNo} seat={seat} />
        ))}
      </div>
      <p style={{ marginTop: 16 }}>
        <Link to="/prizes">查看我的奖品 →</Link>
      </p>
    </div>
  )
}
