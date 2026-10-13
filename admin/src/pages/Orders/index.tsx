import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useRef, useState } from 'react'
import { Button, Input, message, Modal } from 'antd'
import { ErrorAlert } from '../../components/States'

interface OrderRow {
  id: string
  orderNo: string
  userId: string
  status: number
  totalPriceCents: number
  totalCostCents: number
  coinDeduction: number
  couponDeductionCents: number
  createdAtUtc: string
  paidAtUtc?: string | null
}

interface PrizeRow {
  id: string
  marketHashName: string
  rarity: string
  exterior: string
  priceCents: number
  assetId: string
}

const STATUS_LABEL: Record<number, string> = {
  0: '待支付',
  1: '待结算',
  2: '待取回',
  3: '取回中',
  4: '已完成',
  5: '已取消',
  6: '已退款',
}

const fmt = (v: unknown) => String(v ?? '').slice(0, 19).replace('T', ' ')

/** Order management: paged query, refund, prize view. */
export default function OrdersPage() {
  const actionRef = useRef<ActionType>()
  const [refundTarget, setRefundTarget] = useState<OrderRow | null>(null)
  const [reason, setReason] = useState('')
  const [prizeTarget, setPrizeTarget] = useState<OrderRow | null>(null)
  const [prizes, setPrizes] = useState<PrizeRow[]>([])
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  async function confirmRefund() {
    if (!refundTarget) return
    try {
      await request(`/api/admin/orders/${refundTarget.id}/refund`, { method: 'POST', data: { reason } })
      message.success('已退款')
      setRefundTarget(null)
      reload()
    } catch {
      message.error('退款失败：订单当前状态不可退款')
    }
  }

  async function openPrizes(row: OrderRow) {
    setPrizeTarget(row)
    const rows = await request<PrizeRow[]>(`/api/admin/orders/${row.id}/prizes`).catch(() => [])
    setPrizes(rows)
  }

  const columns: ProColumns<OrderRow>[] = [
    { title: '订单号', dataIndex: 'orderNo', width: 190 },
    { title: '状态', dataIndex: 'status', width: 80, render: (_, row) => STATUS_LABEL[row.status] ?? row.status },
    { title: '应付 (元)', dataIndex: 'totalCostCents', width: 95, render: (v) => (Number(v) / 100).toFixed(2) },
    { title: '金币抵扣', dataIndex: 'coinDeduction', width: 85 },
    { title: '券抵扣 (元)', dataIndex: 'couponDeductionCents', width: 95, render: (v) => (Number(v) / 100).toFixed(2) },
    { title: '创建时间', dataIndex: 'createdAtUtc', width: 150, render: fmt },
    {
      title: '操作',
      valueType: 'option',
      width: 130,
      render: (_, row) => [
        <Button key="prizes" size="small" onClick={() => openPrizes(row)}>
          奖品
        </Button>,
        row.status >= 1 && row.status <= 4 ? (
          <Button key="refund" size="small" danger onClick={() => { setReason(''); setRefundTarget(row) }}>
            退款
          </Button>
        ) : null,
      ],
    },
  ]

  return (
    <PageContainer>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<OrderRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async (params) => {
          try {
            const data = await request<{ total: number; items: OrderRow[] }>('/api/admin/orders', {
              method: 'GET',
              params: { page: params.current ?? 1, pageSize: params.pageSize ?? 20 },
            })
            setError('')
            return { data: data.items, total: data.total, success: true }
          } catch (e) {
            setError(e instanceof Error ? e.message : '加载失败')
            return { data: [], total: 0, success: true }
          }
        }}
        search={false}
        pagination={{ pageSize: 20 }}
      />

      <Modal
        title={refundTarget ? `退款：${refundTarget.orderNo}` : ''}
        open={refundTarget !== null}
        onOk={confirmRefund}
        okText="确认退款"
        okButtonProps={{ danger: true }}
        onCancel={() => setRefundTarget(null)}
      >
        <Input.TextArea value={reason} onChange={(e) => setReason(e.target.value)} placeholder="退款原因" rows={3} />
      </Modal>

      <Modal
        title={prizeTarget ? `奖品：${prizeTarget.orderNo}` : ''}
        open={prizeTarget !== null}
        footer={null}
        onCancel={() => setPrizeTarget(null)}
      >
        {prizes.length === 0 ? (
          <p>该订单没有关联奖品（或奖品尚未结算）。</p>
        ) : (
          <table style={{ width: '100%', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ textAlign: 'left' }}>饰品</th>
                <th style={{ textAlign: 'left' }}>品质</th>
                <th style={{ textAlign: 'left' }}>磨损</th>
                <th style={{ textAlign: 'right' }}>价值 (元)</th>
              </tr>
            </thead>
            <tbody>
              {prizes.map((p) => (
                <tr key={p.id}>
                  <td>{p.marketHashName}</td>
                  <td>{p.rarity}</td>
                  <td>{p.exterior || '-'}</td>
                  <td style={{ textAlign: 'right' }}>{(p.priceCents / 100).toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Modal>
    </PageContainer>
  )
}
