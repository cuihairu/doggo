import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorBox, Skeleton, SuccessBox } from '../components/States'

interface OrderView {
  id: string
  orderNo: string
  productId: string
  productName: string
  productIconUrl?: string
  planId?: string | null
  planTitle?: string | null
  totalPriceCents: number
  totalCostCents: number
  coinDeduction: number
  couponDeductionCents: number
  cashbackCents: number
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

const yuan = (cents: number) => `¥${(cents / 100).toFixed(2)}`

function paySecondsLeft(deadline: string): number {
  return Math.max(0, Math.round((new Date(deadline).getTime() - Date.now()) / 1000))
}

/** Countdown badge ticking from the server-set payment deadline. */
function PayCountdown({ deadline }: { deadline: string }) {
  const [seconds, setSeconds] = useState(() => paySecondsLeft(deadline))

  useEffect(() => {
    const timer = setInterval(() => setSeconds(paySecondsLeft(deadline)), 1000)
    return () => clearInterval(timer)
  }, [deadline])

  const label =
    seconds === 0
      ? '已超时'
      : `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`

  return <span style={{ fontWeight: 700 }}>{label}</span>
}

/** Order content card: what is being bought, and at what price. */
function OrderContent({ order }: { order: OrderView }) {
  return (
    <div className="card">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <div style={{ fontSize: 16, fontWeight: 600 }}>订单内容</div>
        <div className="muted" style={{ fontSize: 12 }}>单号 {order.orderNo}</div>
      </div>
      <div style={{ display: 'flex', gap: 14, alignItems: 'center' }}>
        {order.productIconUrl ? (
          <img
            src={order.productIconUrl}
            alt=""
            style={{ width: 64, height: 64, objectFit: 'contain', borderRadius: 'var(--radius-s)', background: 'var(--surface-2)' }}
          />
        ) : (
          <div style={{ width: 64, height: 64, borderRadius: 'var(--radius-s)', background: 'var(--surface-2)', flexShrink: 0 }} />
        )}
        <div style={{ flex: 1 }}>
          <div style={{ fontWeight: 600 }}>{order.productName}</div>
          <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
            套餐：{order.planTitle ?? '单开'} × 1
          </div>
        </div>
        <div style={{ fontSize: 20, color: 'var(--primary-strong)', fontWeight: 700 }}>{yuan(order.totalCostCents)}</div>
      </div>
    </div>
  )
}

/** Payment method list: the mock channel is live, the real one is not wired yet. */
function PaymentMethods() {
  return (
    <div className="card">
      <div style={{ fontSize: 16, fontWeight: 600, marginBottom: 12 }}>支付方式</div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            padding: 12,
            borderRadius: 'var(--radius)',
            border: '1px solid var(--primary)',
          }}
        >
          <div>
            <div style={{ fontWeight: 600 }}>支付宝（模拟）</div>
            <div className="muted" style={{ fontSize: 12 }}>Mock 渠道，支付即回调成功</div>
          </div>
          <span style={{ padding: '2px 8px', borderRadius: 'var(--radius-s)', background: 'var(--success)', color: '#fff', fontSize: 12 }}>
            已选
          </span>
        </div>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: 12, borderRadius: 'var(--radius)', border: '1px solid var(--border)' }}>
          <div>
            <div className="muted" style={{ fontWeight: 600 }}>微信（待接入）</div>
            <div className="muted" style={{ fontSize: 12 }}>真实渠道需商家资质，暂未接入</div>
          </div>
          <span className="muted" style={{ padding: '2px 8px', borderRadius: 'var(--radius-s)', background: 'var(--surface-2)', fontSize: 12 }}>
            暂不可用
          </span>
        </div>
      </div>
    </div>
  )
}

/** Amount breakdown plus the primary action and the cancel fallback. */
function AmountCard({
  order,
  agreed,
  onAgree,
  onPay,
  onCancel,
  busy,
}: {
  order: OrderView
  agreed: boolean
  onAgree: () => void
  onPay: () => void
  onCancel: () => void
  busy: boolean
}) {
  const unpaid = order.status === 0
  return (
    <div className="card">
      <div style={{ fontSize: 16, fontWeight: 600, marginBottom: 12 }}>金额明细</div>
      <table className="table">
        <tbody>
          <tr>
            <td className="muted">商品金额</td>
            <td>{yuan(order.totalPriceCents)}</td>
          </tr>
          {order.couponDeductionCents > 0 && (
            <tr>
              <td className="muted">优惠券</td>
              <td className="price-down">− {yuan(order.couponDeductionCents)}</td>
            </tr>
          )}
          {order.coinDeduction > 0 && (
            <tr>
              <td className="muted">金币抵扣（{order.coinDeduction}）</td>
              <td className="price-down">− {yuan(order.coinDeduction)}</td>
            </tr>
          )}
          <tr>
            <td style={{ fontWeight: 600 }}>应付</td>
            <td style={{ fontSize: 20, color: 'var(--primary-strong)', fontWeight: 700 }}>{yuan(order.totalCostCents)}</td>
          </tr>
        </tbody>
      </table>
      {unpaid ? (
        <div style={{ display: 'flex', gap: 10, marginTop: 14 }}>
          <button className="btn" style={{ flex: 1 }} onClick={onPay} disabled={!agreed || busy}>
            {busy ? '处理中…' : `立即支付 ${yuan(order.totalCostCents)}`}
          </button>
          <button className="btn danger" onClick={onCancel} disabled={busy}>
            取消订单
          </button>
        </div>
      ) : (
        <div className="muted" style={{ marginTop: 14 }}>
          状态：{STATUS_TEXT[order.status] ?? order.status}
        </div>
      )}
      {order.cashbackCents > 0 && unpaid && (
        <div className="muted" style={{ marginTop: 8, fontSize: 12 }}>
          预计返现 {yuan(order.cashbackCents)}，结算后到账
        </div>
      )}
      <div style={{ marginTop: 14, padding: 12, borderRadius: 'var(--radius)', background: 'var(--surface-2)' }}>
        <label style={{ display: 'flex', gap: 8, alignItems: 'flex-start', fontSize: 13, cursor: 'pointer' }}>
          <input type="checkbox" checked={agreed} onChange={onAgree} style={{ width: 16, height: 16, marginTop: 2 }} />
          <span>我已阅读并同意《服务条款》与《开箱规则》，确认按当前价格提交本单</span>
        </label>
      </div>
    </div>
  )
}

/** Three-step flow hint under the form, per the checkout prototype. */
function FlowSteps() {
  return (
    <div className="grid cols-3" style={{ marginTop: 16 }}>
      <div className="card">
        <div style={{ fontWeight: 600 }}>1. 提交订单</div>
        <p className="muted" style={{ margin: '6px 0 0', fontSize: 12 }}>生成订单号，15 分钟内完成支付</p>
      </div>
      <div className="card">
        <div style={{ fontWeight: 600 }}>2. 完成支付</div>
        <p className="muted" style={{ margin: '6px 0 0', fontSize: 12 }}>状态：待支付 → 待结算</p>
      </div>
      <div className="card">
        <div style={{ fontWeight: 600 }}>3. 开箱取货</div>
        <p className="muted" style={{ margin: '6px 0 0', fontSize: 12 }}>结果存入「我的奖品」，取回到 Steam 待 vapor 联调</p>
      </div>
    </div>
  )
}

/** Order payment page: confirm the order, pay, then watch the status roll forward. */
export default function OrderPayPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [order, setOrder] = useState<OrderView | null>(null)
  const [error, setError] = useState('')
  const [paid, setPaid] = useState(false)
  const [agreed, setAgreed] = useState(false)
  const [busy, setBusy] = useState(false)

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
    setBusy(true)
    try {
      await api(`/api/orders/${id}/pay`, { body: {} })
      setPaid(true)
      reload()
    } catch (err) {
      setError(String((err as Error).message ?? err))
    } finally {
      setBusy(false)
    }
  }

  async function cancel() {
    if (!id) return
    setBusy(true)
    try {
      await api(`/api/orders/${id}/cancel`, { body: { reason: 'user cancelled' } })
      setPaid(false)
      reload()
    } catch (err) {
      setError(String((err as Error).message ?? err))
    } finally {
      setBusy(false)
    }
  }

  if (!order) {
    return error ? <ErrorBox detail={error} onRetry={reload} /> : <Skeleton lines={4} />
  }

  const unpaid = order.status === 0

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
        <h1 className="page-title" style={{ margin: 0 }}>确认订单</h1>
        {unpaid && order.payDeadlineUtc && (
          <span style={{ padding: '4px 12px', borderRadius: 'var(--radius-s)', background: 'var(--danger)', color: '#fff', fontSize: 13 }}>
            剩余 <PayCountdown deadline={order.payDeadlineUtc} /> 超时关单
          </span>
        )}
      </div>

      {paid && order.status !== 0 && (
        <div style={{ marginBottom: 14 }}>
          <SuccessBox
            title="支付成功"
            detail={`${yuan(order.totalCostCents)} · 已进入开箱流程`}
            actionLabel="查看我的奖品"
            onAction={() => navigate('/prizes')}
          />
        </div>
      )}

      {error && <p className="error-text">{error}</p>}

      <div className="grid cols-4">
        <div style={{ gridColumn: 'span 3', display: 'flex', flexDirection: 'column', gap: 16 }}>
          <OrderContent order={order} />
          <PaymentMethods />
        </div>
        <div>
          <AmountCard order={order} agreed={agreed} onAgree={() => setAgreed(true)} onPay={pay} onCancel={cancel} busy={busy} />
        </div>
      </div>

      <FlowSteps />
    </div>
  )
}
