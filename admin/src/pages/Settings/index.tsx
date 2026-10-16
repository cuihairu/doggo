import { PageContainer, ProCard } from '@ant-design/pro-components'
import { request, useModel } from '@umijs/max'
import { useCallback, useEffect, useState } from 'react'
import { Button, Form, Input, InputNumber, message, Modal, Space, Switch, Table, Tag, Typography } from 'antd'
import { PageLoading, ErrorAlert, EmptyAction } from '../../components/States'

interface SiteSetting {
  key: string
  value: string
  valueType: string
  note?: string | null
}

interface CrawlMap {
  id: string
  siteKey: string
  externalTitle: string
  externalUrl: string
  marketItemId?: string | null
  mapStatus: number
}

const MAP_STATUS: Record<number, { label: string; color: string }> = {
  0: { label: '待确认', color: 'warning' },
  1: { label: '已确认', color: 'success' },
  2: { label: '已拒绝', color: 'default' },
}

const VALUE_TYPES = ['text', 'number', 'bool', 'json', 'url']

/** Site parameters: key/value rows edited in place, plus adding a new key. */
function SiteSettingsCard() {
  const [rows, setRows] = useState<SiteSetting[]>([])
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [newKey, setNewKey] = useState('')
  const [newValue, setNewValue] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [savingKey, setSavingKey] = useState('')
  const [message, setMessage] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const page = await request<{ total: number; items: SiteSetting[] }>('/api/admin/settings')
      setRows(page.items)
      setDrafts(Object.fromEntries(page.items.map((r) => [r.key, r.value])))
    } catch (e) {
      setError(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function save(key: string, value: string) {
    setSavingKey(key)
    setMessage('')
    try {
      const saved = await request<SiteSetting>(`/api/admin/settings/${encodeURIComponent(key)}`, {
        method: 'PUT',
        data: { value },
      })
      setRows((prev) => [...prev.filter((r) => r.key !== key), saved].sort((a, b) => a.key.localeCompare(b.key)))
      setMessage(`已保存 ${saved.key}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : '保存失败')
    } finally {
      setSavingKey('')
    }
  }

  async function create() {
    const key = newKey.trim()
    if (!key) return
    setSavingKey(key)
    setError('')
    try {
      const saved = await request<SiteSetting>(`/api/admin/settings/${encodeURIComponent(key)}`, {
        method: 'PUT',
        data: { value: newValue.trim() || 'enabled' },
      })
      setRows((prev) => [...prev.filter((r) => r.key !== saved.key), saved].sort((a, b) => a.key.localeCompare(b.key)))
      setDrafts((prev) => ({ ...prev, [saved.key]: saved.value }))
      setNewKey('')
      setNewValue('')
      setMessage(`已新增 ${saved.key}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : '新增失败')
    } finally {
      setSavingKey('')
    }
  }

  return (
    <ProCard
      title="站点参数"
      extra={
        <Space>
          {message ? <Typography.Text type="secondary">{message}</Typography.Text> : null}
          <Button size="small" onClick={load}>
            刷新
          </Button>
        </Space>
      }
    >
      {loading ? (
        <PageLoading />
      ) : error && rows.length === 0 ? (
        <ErrorAlert description={error} onRetry={load} />
      ) : rows.length === 0 ? (
        <EmptyAction description="还没有站点参数" actionLabel="用下方表单新增第一条" onAction={() => document.getElementById('site-setting-new-key')?.focus()} />
      ) : (
        <Table<SiteSetting>
          rowKey="key"
          size="small"
          pagination={false}
          dataSource={rows}
          columns={[
            { title: 'Key', dataIndex: 'key', width: 220 },
            {
              title: '值',
              dataIndex: 'value',
              render: (value: string, row) => (
                <Input
                  value={drafts[row.key] ?? value}
                  onChange={(e) => setDrafts((prev) => ({ ...prev, [row.key]: e.target.value }))}
                  onPressEnter={() => save(row.key, drafts[row.key] ?? value)}
                />
              ),
            },
            { title: '类型', dataIndex: 'valueType', width: 90, render: (v: string) => <Tag>{v}</Tag> },
            { title: '备注', dataIndex: 'note', width: 180, render: (v?: string | null) => v ?? '—' },
            {
              title: '操作',
              width: 90,
              render: (_, row) => (
                <Button
                  type="link"
                  size="small"
                  loading={savingKey === row.key}
                  onClick={() => save(row.key, drafts[row.key] ?? row.value)}
                >
                  保存
                </Button>
              ),
            },
          ]}
        />
      )}

      <Space style={{ marginTop: 16 }} wrap>
        <Input
          id="site-setting-new-key"
          placeholder="新参数 key,如 site.notice"
          style={{ width: 260 }}
          value={newKey}
          onChange={(e) => setNewKey(e.target.value)}
        />
        <Input
          placeholder="值"
          style={{ width: 220 }}
          value={newValue}
          onChange={(e) => setNewValue(e.target.value)}
        />
        <Button type="primary" disabled={!newKey.trim()} loading={savingKey === newKey.trim()} onClick={create}>
          新增/覆盖
        </Button>
      </Space>
      <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0, fontSize: 12 }}>
        key 只允许字母、数字、点、下划线与短横;值类型取值范围 {VALUE_TYPES.join(' / ')}。
      </Typography.Paragraph>
    </ProCard>
  )
}

/** Crawler site overview: how many external listings are mapped and how many still need review. */
function CrawlerSitesCard() {
  const [rows, setRows] = useState<CrawlMap[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setRows(await request<CrawlMap[]>('/api/admin/price-crawl/maps?take=50'))
    } catch (e) {
      setError(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const bySite = rows.reduce<Record<string, { total: number; pending: number }>>((acc, row) => {
    const entry = acc[row.siteKey] ?? { total: 0, pending: 0 }
    entry.total += 1
    if (row.mapStatus === 0) entry.pending += 1
    acc[row.siteKey] = entry
    return acc
  }, {})

  return (
    <ProCard title="爬虫站点总览" extra={<Button size="small" onClick={load}>刷新</Button>}>
      {loading ? (
        <PageLoading rows={2} />
      ) : error ? (
        <ErrorAlert description={error} onRetry={load} />
      ) : rows.length === 0 ? (
        <EmptyAction description="暂无已抓取的站点映射" actionLabel="重新加载" onAction={load} />
      ) : (
        <Space wrap>
          {Object.entries(bySite).map(([site, stat]) => (
            <Tag key={site} color={stat.pending > 0 ? 'warning' : 'success'}>
              {site} · {stat.total} 条映射 · {stat.pending} 条待确认
            </Tag>
          ))}
        </Space>
      )}
      <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0, fontSize: 12 }}>
        站点清单与抓取频控在服务端配置(PricingCrawler 配置节),映射审阅在「价格爬虫」页逐条处理。
      </Typography.Paragraph>
    </ProCard>
  )
}

interface PaymentChannel {
  provider: string
  merchantId: string
  appId: string
  feeBps: number
  secretRef: string
  enabled: boolean
  note?: string | null
}

interface ProblemBody {
  detail?: string
  title?: string
}

const PROVIDER_LABELS: Record<string, string> = {
  alipay: '支付宝',
  wechat: '微信支付',
  unionpay: '银联',
}

/** Server validation text arrives as ProblemDetails; prefer it over a generic message. */
function serverMessage(e: unknown, fallback: string): string {
  const err = e as { data?: ProblemBody; response?: { data?: ProblemBody } }
  const body = err?.data ?? err?.response?.data
  return body?.detail ?? body?.title ?? (e instanceof Error ? e.message : fallback)
}

/** Payment channels (order entities): non-secret channel config, one row per provider; secret values stay in server env vars. */
function PaymentChannelsCard() {
  const [rows, setRows] = useState<PaymentChannel[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState<PaymentChannel | null>(null)
  const [saving, setSaving] = useState(false)
  const [form] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const page = await request<{ total: number; items: PaymentChannel[] }>('/api/admin/payment-channels')
      setRows(page.items)
    } catch (e) {
      setError(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (editing) {
      form.setFieldsValue({
        merchantId: editing.merchantId,
        appId: editing.appId,
        feeBps: editing.feeBps,
        secretRef: editing.secretRef,
        enabled: editing.enabled,
        note: editing.note ?? '',
      })
    }
  }, [editing, form])

  async function save() {
    if (!editing) return
    const values = await form.validateFields()
    setSaving(true)
    try {
      const saved = await request<PaymentChannel>(`/api/admin/payment-channels/${editing.provider}`, {
        method: 'PUT',
        data: {
          merchantId: values.merchantId as string,
          appId: (values.appId as string) ?? '',
          feeBps: (values.feeBps as number) ?? 0,
          secretRef: (values.secretRef as string) ?? '',
          enabled: Boolean(values.enabled),
          note: values.note,
        },
      })
      setRows((prev) => prev.map((r) => (r.provider === saved.provider ? saved : r)))
      message.success(`已保存 ${PROVIDER_LABELS[saved.provider] ?? saved.provider}`)
      setEditing(null)
    } catch (e) {
      message.error(serverMessage(e, '保存失败'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <ProCard title="支付渠道" extra={<Button size="small" onClick={load}>刷新</Button>}>
      {loading ? (
        <PageLoading />
      ) : error && rows.length === 0 ? (
        <ErrorAlert description={error} onRetry={load} />
      ) : rows.length === 0 ? (
        <EmptyAction description="尚未配置任何渠道" actionLabel="重新加载" onAction={load} />
      ) : (
        <Table<PaymentChannel>
          rowKey="provider"
          size="small"
          pagination={false}
          dataSource={rows}
          columns={[
            { title: '渠道', dataIndex: 'provider', width: 110, render: (v: string) => PROVIDER_LABELS[v] ?? v },
            { title: '商户号', dataIndex: 'merchantId', ellipsis: true },
            { title: 'AppId', dataIndex: 'appId', ellipsis: true, render: (v: string) => v || '—' },
            { title: '费率', dataIndex: 'feeBps', width: 90, render: (v: number) => `${v} bps` },
            { title: '密钥引用', dataIndex: 'secretRef', width: 170, ellipsis: true, render: (v: string) => (v ? <Tag>{v}</Tag> : '—') },
            { title: '状态', dataIndex: 'enabled', width: 80, render: (v: boolean) => (v ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>) },
            { title: '备注', dataIndex: 'note', width: 150, ellipsis: true, render: (v?: string | null) => v || '—' },
            {
              title: '操作',
              width: 70,
              render: (_, row) => (
                <Button type="link" size="small" onClick={() => setEditing(row)}>
                  编辑
                </Button>
              ),
            },
          ]}
        />
      )}
      <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0, fontSize: 12 }}>
        费率按基点(1% = 100 bps,0-10000);密钥值不进控制台,这里只登记服务端环境变量名(字母/数字/下划线,≤64 字)。
      </Typography.Paragraph>

      <Modal
        title={editing ? `编辑渠道:${PROVIDER_LABELS[editing.provider] ?? editing.provider}` : ''}
        open={editing !== null}
        onOk={save}
        okText="保存"
        confirmLoading={saving}
        onCancel={() => setEditing(null)}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="merchantId" label="商户号" rules={[{ required: true, message: '商户号 1-64 字' }]}>
            <Input maxLength={64} />
          </Form.Item>
          <Form.Item name="appId" label="AppId">
            <Input maxLength={64} />
          </Form.Item>
          <Form.Item name="feeBps" label="费率(基点)" rules={[{ required: true, message: '0-10000' }]}>
            <InputNumber min={0} max={10000} precision={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="secretRef" label="密钥环境变量名" extra="密钥值只存服务端环境变量,这里只登记变量名">
            <Input maxLength={64} placeholder="如 ALIPAY_APP_SECRET" />
          </Form.Item>
          <Form.Item name="enabled" label="启用" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="note" label="备注">
            <Input maxLength={200} />
          </Form.Item>
        </Form>
      </Modal>
    </ProCard>
  )
}

/** Settings console: editable site parameters, payment channels, crawler overview, and the account card. */
export default function SettingsPage() {
  const { initialState } = useModel('@@initialState')
  return (
    <PageContainer content="可在线修改的站点参数、支付渠道非密钥配置与爬虫概况;渠道密钥值与 Steam 凭据等部署级配置仍在服务端环境变量,不在此修改。">
      <SiteSettingsCard />
      <PaymentChannelsCard />
      <CrawlerSitesCard />
      <ProCard title="当前账号">
        <Typography.Paragraph>登录账号:{initialState?.nickname ?? '-'}（管理员）</Typography.Paragraph>
      </ProCard>
    </PageContainer>
  )
}