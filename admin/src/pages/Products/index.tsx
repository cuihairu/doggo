import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useEffect, useRef, useState } from 'react'
import { Button, Drawer, message, Switch, Tabs, Table, Tag } from 'antd'

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

interface PlanOption {
  id: string
  quantity: number
  minQuantity: number
  priceCents: number
  upliftBps: number
  enabled: boolean
}

interface PlanRow {
  id: string
  title: string
  minQuantity: number
  maxQuantity: number
  canUseCoupon: boolean
  canUseCoin: boolean
  cashbackBps: number
  enabled: boolean
  options: PlanOption[]
}

interface QuantityOption {
  id: string
  quantity: number
  priceCents: number
  alarmThreshold: number
}

interface PlanDetail {
  plans: PlanRow[]
  quantityOptions: QuantityOption[]
}

interface PromotionRow {
  id: string
  productName: string
  discountPriceCents: number
  stock: number
  perUserLimit: number
  dailyReset: boolean
  startAtUtc?: string | null
  endAtUtc?: string | null
  enabled: boolean
}

interface RateRow {
  id: string
  currencyCode: string
  rate: number
  source: string
}

const STATUS_LABEL: Record<number, string> = {
  0: '下架',
  1: '仅自开',
  2: '仅合开',
  3: '上架',
}

function PlanDrawer({ product, onClose }: { product: ProductRow | null; onClose: () => void }) {
  const [detail, setDetail] = useState<PlanDetail | null>(null)

  useEffect(() => {
    setDetail(null)
    if (product) {
      request<PlanDetail>(`/api/admin/products/${product.id}/plans`)
        .then(setDetail)
        .catch(() => setDetail({ plans: [], quantityOptions: [] }))
    }
  }, [product])

  return (
    <Drawer title={product ? `套餐与选项：${product.name}` : ''} width={720} open={product !== null} onClose={onClose}>
      {!detail ? (
        <p>加载中…</p>
      ) : (
        <>
          <Table<PlanRow>
            rowKey="id"
            size="small"
            pagination={false}
            dataSource={detail.plans}
            columns={[
              { title: '套餐', dataIndex: 'title' },
              { title: '数量范围', width: 90, render: (_, r) => `${r.minQuantity}-${r.maxQuantity}` },
              { title: '返现 (bps)', dataIndex: 'cashbackBps', width: 90 },
              { title: '可用券', dataIndex: 'canUseCoupon', width: 70, render: (v) => (v ? '是' : '否') },
              { title: '可用币', dataIndex: 'canUseCoin', width: 70, render: (v) => (v ? '是' : '否') },
              {
                title: '状态',
                dataIndex: 'enabled',
                width: 70,
                render: (v) => (v ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>),
              },
            ]}
            expandable={{
              expandedRowRender: (plan) => (
                <Table<PlanOption>
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={plan.options}
                  columns={[
                    { title: '数量', dataIndex: 'quantity', width: 80 },
                    { title: '最小数量', dataIndex: 'minQuantity', width: 90 },
                    { title: '价格 (元)', dataIndex: 'priceCents', render: (v) => (Number(v) / 100).toFixed(2) },
                    { title: '加价 (bps)', dataIndex: 'upliftBps', width: 90 },
                  ]}
                />
              ),
            }}
          />
          <Table<QuantityOption>
            rowKey="id"
            size="small"
            pagination={false}
            style={{ marginTop: 16 }}
            dataSource={detail.quantityOptions}
            columns={[
              { title: '数量档', dataIndex: 'quantity', width: 100 },
              { title: '价格 (元)', dataIndex: 'priceCents', render: (v) => (Number(v) / 100).toFixed(2) },
              { title: '告警阈值', dataIndex: 'alarmThreshold', width: 100 },
            ]}
          />
        </>
      )}
    </Drawer>
  )
}

function ProductTable() {
  const actionRef = useRef<ActionType>()
  const [planProduct, setPlanProduct] = useState<ProductRow | null>(null)
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<ProductRow>[] = [
    { title: '名称', dataIndex: 'name' },
    { title: 'ClassId', dataIndex: 'classId', width: 140, ellipsis: true },
    { title: '售价 (元)', dataIndex: 'finalPriceCents', width: 100, render: (v) => (Number(v) / 100).toFixed(2) },
    { title: '参考价 (元)', dataIndex: 'referencePriceCents', width: 100, render: (v) => (Number(v) / 100).toFixed(2) },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      render: (_, row) => STATUS_LABEL[row.status] ?? row.status,
    },
    {
      title: '操作',
      valueType: 'option',
      width: 130,
      render: (_, row) => [
        <Button key="plans" size="small" onClick={() => setPlanProduct(row)}>
          套餐
        </Button>,
        <Switch
          key="shelf"
          checked={row.status === 3}
          onChange={async (checked) => {
            await request(`/api/admin/products/${row.id}/shelf`, { method: 'PUT', data: { onShelf: checked } })
            message.success(checked ? '已上架' : '已下架')
            reload()
          }}
        />,
      ],
    },
  ]

  return (
    <>
      <ProTable<ProductRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async () => {
          const rows = await request<ProductRow[]>('/api/admin/products').catch(() => [])
          return { data: rows, total: rows.length, success: true }
        }}
        search={false}
        pagination={{ pageSize: 20 }}
      />
      <PlanDrawer product={planProduct} onClose={() => setPlanProduct(null)} />
    </>
  )
}

function PromotionTable() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<PromotionRow>[] = [
    { title: '商品', dataIndex: 'productName' },
    { title: '特价 (元)', dataIndex: 'discountPriceCents', width: 100, render: (v) => (Number(v) / 100).toFixed(2) },
    { title: '库存', dataIndex: 'stock', width: 80 },
    { title: '限购', dataIndex: 'perUserLimit', width: 80 },
    { title: '每日重置', dataIndex: 'dailyReset', width: 90, render: (v) => (v ? '是' : '否') },
    { title: '开始', dataIndex: 'startAtUtc', width: 150, render: (v) => (v ? String(v).slice(0, 19).replace('T', ' ') : '-') },
    { title: '结束', dataIndex: 'endAtUtc', width: 150, render: (v) => (v ? String(v).slice(0, 19).replace('T', ' ') : '-') },
    {
      title: '启用',
      dataIndex: 'enabled',
      width: 80,
      render: (_, row) => (
        <Switch
          checked={row.enabled}
          onChange={async (checked) => {
            await request(`/api/admin/promotions/${row.id}/enabled`, { method: 'PUT', data: { enabled: checked } })
            message.success(checked ? '已启用' : '已停用')
            reload()
          }}
        />
      ),
    },
  ]

  return (
    <ProTable<PromotionRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        const rows = await request<PromotionRow[]>('/api/admin/promotions').catch(() => [])
        return { data: rows, total: rows.length, success: true }
      }}
      search={false}
    />
  )
}

function RateTable() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<RateRow>[] = [
    { title: '币种', dataIndex: 'currencyCode', width: 120 },
    { title: '汇率', dataIndex: 'rate', width: 140 },
    {
      title: '来源',
      dataIndex: 'source',
      width: 120,
      render: (v) => (v === 'admin' ? <Tag color="gold">人工修正</Tag> : <Tag>{String(v)}</Tag>),
    },
    {
      title: '修正',
      width: 140,
      render: (_, row) => (
        <a
          onClick={() => {
            const input = window.prompt(`修正 ${row.currencyCode} 汇率 (当前 ${row.rate})`)
            if (input === null) return
            const next = Number(input)
            if (!Number.isFinite(next) || next <= 0) {
              message.error('请输入正数')
              return
            }
            request(`/api/admin/exchange-rates/${row.id}`, { method: 'PUT', data: { rate: next } })
              .then(() => {
                message.success('已修正')
                reload()
              })
              .catch(() => message.error('修正失败'))
          }}
        >
          修正汇率
        </a>
      ),
    },
  ]

  return (
    <ProTable<RateRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        const rows = await request<RateRow[]>('/api/admin/exchange-rates').catch(() => [])
        return { data: rows, total: rows.length, success: true }
      }}
      search={false}
    />
  )
}

/** Products, promotions and exchange rates. Plans/options follow the scraper import; shelf and promotions are operator-controlled. */
export default function ProductsPage() {
  return (
    <PageContainer>
      <Tabs
        defaultActiveKey="products"
        items={[
          { key: 'products', label: '商品', children: <ProductTable /> },
          { key: 'promotions', label: '促销', children: <PromotionTable /> },
          { key: 'rates', label: '汇率', children: <RateTable /> },
        ]}
      />
    </PageContainer>
  )
}
