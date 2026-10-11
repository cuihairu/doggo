import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { Button, Input, message, Modal } from 'antd'
import { useRef, useState } from 'react'

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

const STATUS_LABEL: Record<number, { text: string; color: string }> = {
  0: { text: '待支付', color: 'orange' },
  1: { text: '待结算', color: 'blue' },
  2: { text: '待取回', color: 'cyan' },
  3: { text: '取回中', color: 'geekblue' },
  4: { text: '已完成', color: 'green' },
  5: { text: '已取消', color: 'default' },
  6: { text: '已退款', color: 'red' },
}

/** Order management: paged list with the refund action. */
export default function OrdersPage() {
  const actionRef = useRef<ActionType>()
  const [refundTarget, setRefundTarget] = useState<OrderRow | null>(null)
  const [reason, setReason] = useState('')
  const reload = () => actionRef.current?.reload()

  function askRefund(row: OrderRow) {
    setReason('')
    setRefundTarget(row)
  }

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

  const columns: ProColumns<OrderRow>[] = [
    { title: '订单号', dataIndex: 'orderNo', width: 200 },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      render: (_, row) => {
        const label = STATUS_LABEL[row.status] ?? { text: String(row.status), color: 'default' }
        return <span style={{ color: label.color === 'default' ? undefined : label.color }}>{label.text}</span>
      },
    },
    { title: '应付 (元)', dataIndex: 'totalCostCents', width: 100, render: (v) => (Number(v) / 100).toFixed(2) },
    { title: '金币抵扣', dataIndex: 'coinDeduction', width: 90 },
    { title: '券抵扣 (元)', dataIndex: 'couponDeductionCents', width: 100, render: (v) => (Number(v) / 100).toFixed(2) },
    {
      title: '创建时间',
      dataIndex: 'createdAtUtc',
      width: 160,
      render: (v) => String(v).slice(0, 19).replace('T', ' '),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 100,
      render: (_, row) =>
        row.status >= 1 && row.status <= 4 ? [<Button key="refund" size="small" danger onClick={() => askRefund(row)}>退款</Button>] : [],
    },
  ]

  return (
    <PageContainer>
      <ProTable<OrderRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async (params) => {
          const page = params.current ?? 1
          const pageSize = params.pageSize ?? 20
          const data = await request<{ total: number; items: OrderRow[] }>('/api/admin/orders', {
            method: 'GET',
            params: { page, pageSize },
          }).catch(() => ({ total: 0, items: [] }))
          return { data: data.items, total: data.total, success: true }
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
        <Input.TextArea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="退款原因"
          rows={3}
        />
      </Modal>
    </PageContainer>
  )
}
