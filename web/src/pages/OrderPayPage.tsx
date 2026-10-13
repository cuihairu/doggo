import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorBox, Skeleton, SuccessBox } from '../components/States'

interface OrderView {
  id: string
  orderNo: string
  totalPriceCents: number
  totalCostCents: number
  coinDeduction: number
  couponDeductionCents: number
  status: number
  payDeadlineUtc?: string
}

const STATUS_TEXT: Record<number, string> = {
  0: '待支付',
  1: '待结算',
  2: '待取回',
  3: '取回中',
  4: '已完成',
  5: '已取消',
  6: '已退款',
}

/** Order payment page: pay, watch the status, or cancel while unpaid. */
export default function OrderPayPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [order, setOrder] = useState<OrderView | null>(null)
  const [error, setError] = useState('')
  const [paid, setPaid] = useState(false)

  const reload = useCallback(() => {
    if (!id) return
    api<OrderView>(`/api/orders/${id}`).then(setOrder).catch((err) => setError(String(err.message ?? err)))
  }, [id])

  useEffect(() => {
    reload()
    const timer = setInterval(reload, 4000)
    return () => clearInterval(timer)
  }, [reload])

  async function pay() {
    if (!id) return
    try {
      await api(`/api/orders/${id}/pay`, { body: {} })
      setPaid(true)
      reload()
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  async function cancel() {
    if (!id) return
    try {
      await api(`/api/orders/${id}/cancel`, { body: { reason: 'user cancelled' } })
      setPaid(false)
      reload()
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  if (!order) {
    return error ? <ErrorBox detail={error} onRetry={reload} /> : <Skeleton lines={4} />
  }

  return (
    <div>
      <h1 className="page-title">订单支付</h1>
      {paid && order.status !== 0 && (
        <SuccessBox
          title="支付成功"
          detail={`¥ ${(order.totalCostCents / 100).toFixed(2)} · 已进入开箱流程`}
          actionLabel="查看我的奖品"
          onAction={() => navigate('/prizes')}
        />
      )}
      <div className="card">
        <p>订单号 {order.orderNo}</p>
        <p>
          应付 <strong>¥{(order.totalCostCents / 100).toFixed(2)}</strong>
          {order.coinDeduction > 0 && <span className="muted">（积分抵扣 {order.coinDeduction}）</span>}
          {order.couponDeductionCents > 0 && <span className="muted">（优惠券抵扣 ¥{(order.couponDeductionCents / 100).toFixed(2)}）</span>}
        </p>
        <p>状态 {STATUS_TEXT[order.status] ?? order.status}</p>
        {order.payDeadlineUtc && order.status === 0 && (
          <p className="muted">支付截止 {order.payDeadlineUtc.slice(0, 19).replace('T', ' ')}</p>
        )}
        {error && <p className="error-text">{error}</p>}
        {order.status === 0 && (
          <div style={{ display: 'flex', gap: 10 }}>
            <button className="btn" onClick={pay}>模拟支付</button>
            <button className="btn danger" onClick={cancel}>取消订单</button>
          </div>
        )}
      </div>
    </div>
  )
}
