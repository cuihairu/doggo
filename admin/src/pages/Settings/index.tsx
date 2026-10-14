import { PageContainer, ProCard } from '@ant-design/pro-components'
import { request, useModel } from '@umijs/max'
import { useCallback, useEffect, useState } from 'react'
import { Alert, Button, Input, Space, Table, Tag, Typography } from 'antd'
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

/** Settings console: editable site parameters, crawler overview, and the account card. */
export default function SettingsPage() {
  const { initialState } = useModel('@@initialState')
  return (
    <PageContainer content="可在线修改的站点参数与爬虫概况;支付渠道、Steam 凭据等部署级配置仍在服务端环境变量,不在此修改。">
      <SiteSettingsCard />
      <CrawlerSitesCard />
      <ProCard title="当前账号">
        <Typography.Paragraph>登录账号:{initialState?.nickname ?? '-'}（管理员）</Typography.Paragraph>
        <Alert
          type="info"
          showIcon
          message="支付渠道与运营位日历的控制台入口待接"
          description="服务端已提供渠道配置实体与 Banner 投放起止区间;控制台页面尚未挂载,故此处不提供入口。渠道密钥值仍只在服务端环境变量,不进控制台。"
        />
      </ProCard>
    </PageContainer>
  )
}