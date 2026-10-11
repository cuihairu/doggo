import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { message, Switch } from 'antd'
import { useRef } from 'react'

interface ProductRow {
  id: string
  type: number
  name: string
  classId: string
  finalPriceCents: number
  referencePriceCents: number
  status: number
  sortOrder: number
}

const STATUS_LABEL: Record<number, string> = {
  0: '下架',
  1: '仅自开',
  2: '仅合开',
  3: '上架',
}

/** Product shelf control: flip between OnShelf(3) and OffShelf(0). */
export default function ProductsPage() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<ProductRow>[] = [
    { title: '名称', dataIndex: 'name' },
    { title: 'ClassId', dataIndex: 'classId', width: 160, ellipsis: true },
    { title: '售价 (元)', dataIndex: 'finalPriceCents', width: 110, render: (v) => (Number(v) / 100).toFixed(2) },
    { title: '参考价 (元)', dataIndex: 'referencePriceCents', width: 110, render: (v) => (Number(v) / 100).toFixed(2) },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      render: (_, row) => STATUS_LABEL[row.status] ?? row.status,
    },
    {
      title: '上架',
      width: 90,
      render: (_, row) => (
        <Switch
          checked={row.status === 3}
          onChange={async (checked) => {
            await request(`/api/admin/products/${row.id}/shelf`, { method: 'PUT', data: { onShelf: checked } })
            message.success(checked ? '已上架' : '已下架')
            reload()
          }}
        />
      ),
    },
  ]

  return (
    <PageContainer>
      <ProTable<ProductRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async () => {
          const rows = await request<ProductRow[]>('/api/admin/products').catch(() => [])
          return { data: rows, total: rows.length, success: true }
        }}
        search={false}
        options={{ reload: true }}
      />
    </PageContainer>
  )
}
