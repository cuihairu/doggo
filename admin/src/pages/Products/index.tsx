import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useEffect, useRef, useState } from 'react'
import {
  Button,
  Drawer,
  Form,
  Input,
  InputNumber,
  Select,
  Switch,
  Tabs,
  Tag,
  message,
} from 'antd'

const TYPE_OPTIONS = [
  { value: 0, label: '钥匙' },
  { value: 1, label: '箱子' },
  { value: 2, label: '箱子组件' },
]

const STATUS_OPTIONS = [
  { value: 0, label: '下架' },
  { value: 1, label: '仅自开' },
  { value: 2, label: '仅合开' },
  { value: 3, label: '上架' },
]

const QUALITY_OPTIONS = [
  { value: 1, label: '消费级' },
  { value: 2, label: '工业级' },
  { value: 3, label: '军规' },
  { value: 4, label: '受限' },
  { value: 5, label: '机密' },
  { value: 6, label: '隐秘' },
  { value: 7, label: '罕见特殊' },
]

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

interface PlanOptionEdit {
  upliftBps: number
  quantity: number
  minQuantity: number
  priceCents: number
  enabled: boolean
}

interface PlanEdit {
  title: string
  minQuantity: number
  maxQuantity: number
  canUseCoupon: boolean
  canUseCoin: boolean
  cashbackBps: number
  enabled: boolean
  options: PlanOptionEdit[]
}

interface PlanDetail {
  plans: PlanEdit[]
  quantityOptions: { id: string; quantity: number; priceCents: number; alarmThreshold: number }[]
  contents: {
    id: string
    title: string
    quality: number
    color: string
    imageUrl: string
    referencePriceCents: number
    sortOrder: number
  }[]
}

interface ContentEdit {
  title: string
  quality: number
  color: string
  imageUrl: string
  referencePriceCents: number
  sortOrder: number
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

const toYuan = (cents: number) => Number(cents) / 100
const toCents = (yuan: number) => Math.round(Number(yuan) * 100)

function Num({ value, onChange, width = 96 }: { value: number; onChange: (v: number) => void; width?: number }) {
  return (
    <InputNumber
      size="small"
      value={value}
      min={0}
      style={{ width }}
      onChange={(v) => onChange(Number(v) ?? 0)}
    />
  )
}

/** Create/edit drawer for the product's basic fields. */
function ProductFormDrawer({
  product,
  open,
  onClose,
  onSaved,
}: {
  product: ProductRow | null
  open: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const [form] = Form.useForm()
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (product) {
      form.setFieldsValue({ ...product })
    } else {
      form.setFieldsValue({ type: 1, status: 0, sortOrder: 0 })
    }
  }, [product, form])

  const submit = async () => {
    const values = await form.validateFields()
    setSaving(true)
    try {
      if (product) {
        await request(`/api/admin/products/${product.id}`, {
          method: 'PUT',
          data: { ...values, type: Number(values.type), status: Number(values.status) },
        })
        message.success('已保存')
      } else {
        await request('/api/admin/products', {
          method: 'POST',
          data: { ...values, type: Number(values.type), status: Number(values.status) },
        })
        message.success('已创建')
      }
      onSaved()
      onClose()
    } catch {
      message.error('保存失败')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Drawer
      title={product ? `编辑商品：${product.name}` : '新建商品'}
      width={520}
      open={open}
      onClose={onClose}
      extra={
        <Button type="primary" loading={saving} onClick={submit}>
          保存
        </Button>
      }
    >
      <Form form={form} layout="vertical">
        <Form.Item name="name" label="名称" rules={[{ required: true, message: '请输入名称' }]}>
          <Input placeholder="如：梦魇武器箱" />
        </Form.Item>
        <Form.Item name="type" label="类型">
          <Select options={TYPE_OPTIONS} />
        </Form.Item>
        <Form.Item name="classId" label="ClassId">
          <Input placeholder="Steam 物品 classId" />
        </Form.Item>
        <Form.Item name="iconUrl" label="图标地址">
          <Input placeholder="https://…" />
        </Form.Item>
        <Form.Item label="价格（元）">
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
            <Form.Item name="purchasePriceCents" label="采购价" style={{ marginBottom: 0 }}>
              <InputNumber style={{ width: 110 }} min={0} precision={2} />
            </Form.Item>
            <Form.Item name="referencePriceCents" label="参考价" style={{ marginBottom: 0 }}>
              <InputNumber style={{ width: 110 }} min={0} precision={2} />
            </Form.Item>
            <Form.Item name="adjustPriceCents" label="价格修正" style={{ marginBottom: 0 }}>
              <InputNumber style={{ width: 110 }} min={0} precision={2} />
            </Form.Item>
            <Form.Item name="finalPriceCents" label="最终价" style={{ marginBottom: 0 }}>
              <InputNumber style={{ width: 110 }} min={0} precision={2} />
            </Form.Item>
            <Form.Item name="regularPriceCents" label="原价" style={{ marginBottom: 0 }}>
              <InputNumber style={{ width: 110 }} min={0} precision={2} />
            </Form.Item>
          </div>
        </Form.Item>
        <Form.Item name="status" label="上架状态">
          <Select options={STATUS_OPTIONS} />
        </Form.Item>
        <Form.Item name="planFlags" label="套餐标记（逗号分隔）">
          <Input placeholder="如：hot,new" />
        </Form.Item>
        <Form.Item name="buyNotice" label="购买须知">
          <Input.TextArea rows={2} />
        </Form.Item>
        <Form.Item name="sortOrder" label="排序值">
          <InputNumber style={{ width: 110 }} />
        </Form.Item>
      </Form>
    </Drawer>
  )
}

function PlanSection({
  product,
  plans,
  setPlans,
  onSaved,
}: {
  product: ProductRow
  plans: PlanEdit[]
  setPlans: (plans: PlanEdit[]) => void
  onSaved: () => void
}) {
  const [saving, setSaving] = useState(false)

  const update = (i: number, patch: Partial<PlanEdit>) =>
    setPlans(plans.map((p, idx) => (idx === i ? { ...p, ...patch } : p)))

  const updateOption = (i: number, j: number, patch: Partial<PlanOptionEdit>) =>
    setPlans(plans.map((p, idx) => (idx === i ? { ...p, options: p.options.map((o, k) => (k === j ? { ...o, ...patch } : o)) } : p)))

  const save = async () => {
    setSaving(true)
    try {
      await request(`/api/admin/products/${product.id}/plans`, {
        method: 'PUT',
        data: { plans },
      })
      message.success('套餐已保存')
      onSaved()
    } catch {
      message.error('保存失败')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section style={{ marginBottom: 24 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
        <b>套餐与选项</b>
        <span style={{ display: 'flex', gap: 8 }}>
          <Button
            size="small"
            onClick={() =>
              setPlans([
                ...plans,
                { title: '', minQuantity: 1, maxQuantity: 1, canUseCoupon: false, canUseCoin: false, cashbackBps: 0, enabled: true, options: [] },
              ])
            }
          >
            添加套餐
          </Button>
          <Button size="small" type="primary" loading={saving} onClick={save}>
            保存套餐
          </Button>
        </span>
      </div>
      {plans.length === 0 && <p style={{ color: 'var(--muted)' }}>暂无套餐。点「添加套餐」新建。</p>}
      {plans.map((plan, i) => (
        <div
          key={i}
          style={{ border: '1px solid var(--border)', borderRadius: 6, padding: 12, marginBottom: 8 }}
        >
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
            <Input size="small" value={plan.title} placeholder="套餐名，如 3连开" style={{ width: 140 }} onChange={(e) => update(i, { title: e.target.value })} />
            <span style={{ fontSize: 12 }}>
              起批 <Num value={plan.minQuantity} onChange={(v) => update(i, { minQuantity: v })} width={72} /> ~
              <Num value={plan.maxQuantity} onChange={(v) => update(i, { maxQuantity: v })} width={72} /> 止
            </span>
            <span style={{ fontSize: 12 }}>返现 bps</span>
            <Num value={plan.cashbackBps} onChange={(v) => update(i, { cashbackBps: v })} width={72} />
            <label style={{ fontSize: 12 }}>
              用券
              <Switch size="small" checked={plan.canUseCoupon} style={{ marginLeft: 4 }} onChange={(v) => update(i, { canUseCoupon: v })} />
            </label>
            <label style={{ fontSize: 12 }}>
              用币
              <Switch size="small" checked={plan.canUseCoin} style={{ marginLeft: 4 }} onChange={(v) => update(i, { canUseCoin: v })} />
            </label>
            <label style={{ fontSize: 12 }}>
              启用
              <Switch size="small" checked={plan.enabled} style={{ marginLeft: 4 }} onChange={(v) => update(i, { enabled: v })} />
            </label>
            <Button size="small" danger onClick={() => setPlans(plans.filter((_, idx) => idx !== i))}>
              删除
            </Button>
          </div>
          {plan.options.length > 0 && (
            <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
              <span style={{ fontSize: 12, color: 'var(--muted)' }}>
                选项（数量 / 最小数量 / 价格元 / 加价 bps）
              </span>
              {plan.options.map((option, j) => (
                <div key={j} style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <Num value={option.quantity} onChange={(v) => updateOption(i, j, { quantity: v })} width={72} />
                  <Num value={option.minQuantity} onChange={(v) => updateOption(i, j, { minQuantity: v })} width={72} />
                  <Num value={toYuan(option.priceCents)} onChange={(v) => updateOption(i, j, { priceCents: toCents(v) })} width={84} />
                  <Num value={option.upliftBps} onChange={(v) => updateOption(i, j, { upliftBps: v })} width={72} />
                  <Switch size="small" checked={option.enabled} onChange={(v) => updateOption(i, j, { enabled: v })} />
                  <Button size="small" danger onClick={() => update(i, { options: plan.options.filter((_, k) => k !== j) })}>
                    删除
                  </Button>
                </div>
              ))}
            </div>
          )}
          <Button
            size="small"
            style={{ marginTop: 8 }}
            onClick={() => update(i, { options: [...plan.options, { upliftBps: 0, quantity: 1, minQuantity: 1, priceCents: 0, enabled: true }] })}
          >
            添加选项
          </Button>
        </div>
      ))}
    </section>
  )
}

function TierSection({
  product,
  tiers,
  setTiers,
  onSaved,
}: {
  product: ProductRow
  tiers: { quantity: number; priceCents: number; alarmThreshold: number }[]
  setTiers: (tiers: { quantity: number; priceCents: number; alarmThreshold: number }[]) => void
  onSaved: () => void
}) {
  const [saving, setSaving] = useState(false)

  const update = (i: number, patch: Partial<{ quantity: number; priceCents: number; alarmThreshold: number }>) =>
    setTiers(tiers.map((t, idx) => (idx === i ? { ...t, ...patch } : t)))

  const save = async () => {
    setSaving(true)
    try {
      await request(`/api/admin/products/${product.id}/quantity-options`, {
        method: 'PUT',
        data: { options: tiers },
      })
      message.success('数量档已保存')
      onSaved()
    } catch {
      message.error('保存失败')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section style={{ marginBottom: 24 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
        <b>数量档位与库存告警</b>
        <span style={{ display: 'flex', gap: 8 }}>
          <Button size="small" onClick={() => setTiers([...tiers, { quantity: 1, priceCents: 0, alarmThreshold: 0 }])}>
            添加档位
          </Button>
          <Button size="small" type="primary" loading={saving} onClick={save}>
            保存档位
          </Button>
        </span>
      </div>
      {tiers.length === 0 && <p style={{ color: 'var(--muted)' }}>暂无档位。</p>}
      {tiers.map((tier, i) => (
        <div key={i} style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
          <span style={{ fontSize: 12 }}>数量</span>
          <Num value={tier.quantity} onChange={(v) => update(i, { quantity: v })} width={72} />
          <span style={{ fontSize: 12 }}>价格 (元)</span>
          <Num value={toYuan(tier.priceCents)} onChange={(v) => update(i, { priceCents: toCents(v) })} width={84} />
          <span style={{ fontSize: 12 }}>告警阈值</span>
          <Num value={tier.alarmThreshold} onChange={(v) => update(i, { alarmThreshold: v })} width={72} />
          <Button size="small" danger onClick={() => setTiers(tiers.filter((_, idx) => idx !== i))}>
            删除
          </Button>
        </div>
      ))}
    </section>
  )
}

function ContentSection({
  product,
  contents,
  setContents,
  onSaved,
}: {
  product: ProductRow
  contents: ContentEdit[]
  setContents: (contents: ContentEdit[]) => void
  onSaved: () => void
}) {
  const [saving, setSaving] = useState(false)

  const update = (i: number, patch: Partial<ContentEdit>) =>
    setContents(contents.map((c, idx) => (idx === i ? { ...c, ...patch } : c)))

  const save = async () => {
    setSaving(true)
    try {
      await request(`/api/admin/products/${product.id}/contents`, {
        method: 'PUT',
        data: { contents },
      })
      message.success('内容物已保存')
      onSaved()
    } catch {
      message.error('保存失败')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
        <b>箱子内容物（仅展示，不参与掉落判定）</b>
        <span style={{ display: 'flex', gap: 8 }}>
          <Button
            size="small"
            onClick={() => setContents([...contents, { title: '', quality: 1, color: '', imageUrl: '', referencePriceCents: 0, sortOrder: contents.length + 1 }])}
          >
            添加内容物
          </Button>
          <Button size="small" type="primary" loading={saving} onClick={save}>
            保存内容物
          </Button>
        </span>
      </div>
      {contents.length === 0 && <p style={{ color: 'var(--muted)' }}>暂无内容物。</p>}
      {contents.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={{ fontSize: 12, color: 'var(--muted)' }}>标题 / 品质 / 颜色 / 参考价元 / 排序 / 图片</span>
          {contents.map((content, i) => (
            <div key={i} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
              <Input size="small" value={content.title} placeholder="如 AK-47 | Redline" style={{ width: 180 }} onChange={(e) => update(i, { title: e.target.value })} />
              <Select size="small" value={content.quality} options={QUALITY_OPTIONS} style={{ width: 100 }} onChange={(v) => update(i, { quality: Number(v) })} />
              <Input size="small" value={content.color} placeholder="#eb4b4b" style={{ width: 80 }} onChange={(e) => update(i, { color: e.target.value })} />
              <Num value={toYuan(content.referencePriceCents)} onChange={(v) => update(i, { referencePriceCents: toCents(v) })} width={84} />
              <Num value={content.sortOrder} onChange={(v) => update(i, { sortOrder: v })} width={56} />
              <Input size="small" value={content.imageUrl} placeholder="https://…" style={{ width: 160 }} onChange={(e) => update(i, { imageUrl: e.target.value })} />
              <Button size="small" danger onClick={() => setContents(contents.filter((_, idx) => idx !== i))}>
                删除
              </Button>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

/** Editable view of plans, quantity tiers and contents; one PUT per group (full-set replace). */
function DataDrawer({
  product,
  onClose,
  onSaved,
}: {
  product: ProductRow | null
  onClose: () => void
  onSaved: () => void
}) {
  const [detail, setDetail] = useState<PlanDetail | null>(null)
  const [plans, setPlans] = useState<PlanEdit[]>([])
  const [tiers, setTiers] = useState<{ quantity: number; priceCents: number; alarmThreshold: number }[]>([])
  const [contents, setContents] = useState<ContentEdit[]>([])

  useEffect(() => {
    setDetail(null)
    if (product) {
      request<PlanDetail>(`/api/admin/products/${product.id}/plans`)
        .then((data) => {
          setDetail(data)
          setPlans(data.plans)
          setTiers(data.quantityOptions)
          setContents(data.contents)
        })
        .catch(() => message.error('加载失败'))
    }
  }, [product])

  return (
    <Drawer title={product ? `商品资料：${product.name}` : ''} width={880} open={product !== null} onClose={onClose}>
      {!detail ? (
        <p>加载中…</p>
      ) : (
        product && (
          <>
            <PlanSection product={product} plans={plans} setPlans={setPlans} onSaved={onSaved} />
            <TierSection product={product} tiers={tiers} setTiers={setTiers} onSaved={onSaved} />
            <ContentSection product={product} contents={contents} setContents={setContents} onSaved={onSaved} />
          </>
        )
      )}
    </Drawer>
  )
}

function ProductTable() {
  const actionRef = useRef<ActionType>()
  const [planProduct, setPlanProduct] = useState<ProductRow | null>(null)
  const [formProduct, setFormProduct] = useState<ProductRow | null>(null)
  const [formOpen, setFormOpen] = useState(false)
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
      width: 200,
      render: (_, row) => [
        <Button
          key="edit"
          size="small"
          onClick={() => {
            setFormProduct(row)
            setFormOpen(true)
          }}
        >
          编辑
        </Button>,
        <Button key="data" size="small" onClick={() => setPlanProduct(row)}>
          资料
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
        toolBarRender={() => [
          <Button
            key="create"
            type="primary"
            onClick={() => {
              setFormProduct(null)
              setFormOpen(true)
            }}
          >
            新建商品
          </Button>,
        ]}
      />
      <ProductFormDrawer product={formProduct} open={formOpen} onClose={() => setFormOpen(false)} onSaved={reload} />
      <DataDrawer product={planProduct} onClose={() => setPlanProduct(null)} onSaved={reload} />
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
      pagination={{ pageSize: 20 }}
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
      pagination={{ pageSize: 20 }}
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
