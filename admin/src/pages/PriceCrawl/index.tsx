import { PageContainer, ProCard, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useRef, useState } from 'react'
import { Button, Col, Row, Segmented, Space, Tag, message } from 'antd'
import { EmptyAction, ErrorAlert } from '../../components/States'

interface CrawlMapRow {
  id: string
  siteKey: string
  externalId: string
  externalTitle: string
  externalUrl: string
  marketItemId?: string | null
  mapStatus: number
  matchSource: number
}

interface SpreadRow {
  marketItemId: string
  siteCount: number
  minCents: number
  maxCents: number
  spreadCents: number
  spreadPct: number
}

const MAP_STATUS: Record<number, { text: string; color: string }> = {
  0: { text: '待确认', color: 'warning' },
  1: { text: '已确认', color: 'success' },
  2: { text: '已拒绝', color: 'default' },
}

const MATCH_SOURCE: Record<number, string> = {
  0: '类名',
  1: '哈希名',
  2: '人工',
}

const yuan = (cents: number) => `¥${(cents / 100).toFixed(2)}`

/**
 * Price crawler console (price-crawler-design §8): mapping review plus the
 * spread report. Both read admin endpoints; nothing here writes prices back.
 */
export default function PriceCrawlPage() {
  const [status, setStatus] = useState<number>(0)
  const [days, setDays] = useState<number>(7)
  const [mapError, setMapError] = useState('')
  const [spreadError, setSpreadError] = useState('')
  const [busyId, setBusyId] = useState('')
  const mapRef = useRef<ActionType>()
  const spreadRef = useRef<ActionType>()

  async function review(id: string, decision: 'confirm' | 'reject') {
    setBusyId(id)
    try {
      await request(`/api/admin/price-crawl/maps/${id}/${decision}`, { method: 'POST' })
      message.success(decision === 'confirm' ? '已确认映射' : '已拒绝映射')
      mapRef.current?.reload()
    } catch (e) {
      message.error(e instanceof Error ? e.message : '操作失败')
    } finally {
      setBusyId('')
    }
  }

  const mapColumns: ProColumns<CrawlMapRow>[] = [
    { title: '站点', dataIndex: 'siteKey', width: 120 },
    { title: '外部标题', dataIndex: 'externalTitle', ellipsis: true },
    { title: '外部 ID', dataIndex: 'externalId', width: 160, ellipsis: true },
    {
      title: '站内商品',
      dataIndex: 'marketItemId',
      width: 200,
      render: (_, row) => (row.marketItemId ? <Tag color="blue">{row.marketItemId.slice(0, 8)}</Tag> : <Tag>未匹配</Tag>),
    },
    { title: '匹配方式', dataIndex: 'matchSource', width: 90, render: (v: number) => MATCH_SOURCE[v] ?? v },
    {
      title: '状态',
      dataIndex: 'mapStatus',
      width: 90,
      render: (v: number) => <Tag color={MAP_STATUS[v]?.color}>{MAP_STATUS[v]?.text ?? v}</Tag>,
    },
    {
      title: '操作',
      width: 130,
      render: (_, row) => (
        <Space>
          <Button size="small" type="link" disabled={row.mapStatus === 1} loading={busyId === row.id} onClick={() => review(row.id, 'confirm')}>
            确认
          </Button>
          <Button size="small" type="link" danger disabled={row.mapStatus === 2} loading={busyId === row.id} onClick={() => review(row.id, 'reject')}>
            拒绝
          </Button>
        </Space>
      ),
    },
  ]

  const spreadColumns: ProColumns<SpreadRow>[] = [
    { title: '站内商品', dataIndex: 'marketItemId', width: 220, render: (v: string) => <Tag color="blue">{v.slice(0, 8)}</Tag> },
    { title: '站点数', dataIndex: 'siteCount', width: 90 },
    { title: '最低价', dataIndex: 'minCents', render: (v: number) => yuan(v) },
    { title: '最高价', dataIndex: 'maxCents', render: (v: number) => yuan(v) },
    { title: '价差', dataIndex: 'spreadCents', render: (v: number) => yuan(v) },
    {
      title: '价差比例',
      dataIndex: 'spreadPct',
      render: (v: number) => <span style={{ color: v > 0 ? '#d83931' : '#2e9e6b' }}>{(v * 100).toFixed(2)}%</span>,
    },
  ]

  return (
    <PageContainer content="第三方站点价格的映射审阅与站内价差对照。爬取数据只读对照，不回写站内行情快照。">
      <Row gutter={16}>
        <Col span={24} lg={16}>
          <ProCard title="映射审阅" style={{ marginBottom: 16 }}>
            <Segmented
              style={{ marginBottom: 12 }}
              value={status}
              onChange={(v) => setStatus(v as number)}
              options={[
                { value: 0, label: '待确认' },
                { value: 1, label: '已确认' },
                { value: 2, label: '已拒绝' },
              ]}
            />
            <ProTable<CrawlMapRow>
              actionRef={mapRef}
              rowKey="id"
              size="small"
              search={false}
              options={false}
              toolBarRender={false}
              columns={mapColumns}
              request={async () => {
                setMapError('')
                try {
                  const rows = await request<CrawlMapRow[]>(`/api/admin/price-crawl/maps?status=${status}&take=100`)
                  return { data: rows, total: rows.length, success: true }
                } catch (e) {
                  setMapError(e instanceof Error ? e.message : '加载失败')
                  return { data: [], total: 0, success: true }
                }
              }}
            />
            {mapError ? <ErrorAlert description={mapError} onRetry={() => mapRef.current?.reload()} /> : null}
          </ProCard>
        </Col>
        <Col span={24} lg={8}>
          <ProCard
            title="站内 vs 外部比价"
            extra={
              <Segmented
                value={days}
                onChange={(v) => setDays(v as number)}
                options={[
                  { value: 7, label: '7 天' },
                  { value: 30, label: '30 天' },
                ]}
              />
            }
          >
            <ProTable<SpreadRow>
              actionRef={spreadRef}
              rowKey="marketItemId"
              size="small"
              search={false}
              options={false}
              toolBarRender={false}
              columns={spreadColumns}
              pagination={{ pageSize: 10, showSizeChanger: false }}
              locale={{
                emptyText: (
                  <EmptyAction description="区间内没有可比对的站点价格" actionLabel="重新加载" onAction={() => spreadRef.current?.reload()} />
                ),
              }}
              request={async () => {
                setSpreadError('')
                try {
                  const rows = await request<SpreadRow[]>(`/api/admin/price-crawl/spread-report?days=${days}`)
                  return { data: rows, total: rows.length, success: true }
                } catch (e) {
                  setSpreadError(e instanceof Error ? e.message : '加载失败')
                  return { data: [], total: 0, success: true }
                }
              }}
            />
            {spreadError ? <ErrorAlert description={spreadError} onRetry={() => spreadRef.current?.reload()} /> : null}
          </ProCard>
        </Col>
      </Row>
    </PageContainer>
  )
}