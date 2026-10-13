import { PageContainer, ProCard } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useCallback, useEffect, useState } from 'react'
import { Col, DatePicker, Row, Select, Space, Table, Typography, theme as antdTheme } from 'antd'
import dayjs, { Dayjs } from 'dayjs'
import { EmptyAction, ErrorAlert, PageLoading } from '../../components/States'

interface FunnelStep {
  step: string
  label: string
  uv: number
  rate: number
  dropRate: number
}

interface FunnelGroup {
  key: string
  steps: FunnelStep[]
}

interface FunnelRow {
  date: string
  client: string
  channel: string
  viewProductUv: number
  checkoutIntentUv: number
  orderCreatedUv: number
  orderPaidUv: number
}

interface FunnelReport {
  from: string
  to: string
  grain: string
  groupBy: string
  groups: FunnelGroup[]
  rows: FunnelRow[]
  receivedToday: number
}

const STEP_NAMES = ['page_view_product', 'checkout_intent', 'order_created', 'order_paid']

/** Horizontal step bars, pure div rectangles (funnel design §8.2): bar length = UV,
 * tail shows the UV and the step-over-step conversion. No chart library. */
function FunnelBars({ steps }: { steps: FunnelStep[] }) {
  const { token } = antdTheme.useToken()
  // Brand ramp: solid → hover → border → bg, so it follows any theme's primary.
  const ramp = [token.colorPrimary, token.colorPrimaryHover, token.colorPrimaryBorder, token.colorPrimaryBg]
  const max = Math.max(...steps.map((s) => s.uv), 1)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {steps.map((step, i) => (
        <div key={step.step} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ width: 64, textAlign: 'right', color: token.colorTextSecondary }}>{step.label}</span>
          <div
            style={{
              height: 28,
              width: `${Math.max((step.uv / max) * 100, 2)}%`,
              minWidth: 40,
              background: ramp[i] ?? token.colorPrimary,
              borderRadius: 4,
            }}
          />
          <span style={{ whiteSpace: 'nowrap' }}>
            <b>{step.uv.toLocaleString()}</b>
            <Typography.Text type="secondary" style={{ marginLeft: 8 }}>
              {i === 0 ? '—' : `转化 ${(step.rate * 100).toFixed(1)}% / 流失 ${(step.dropRate * 100).toFixed(1)}%`}
            </Typography.Text>
          </span>
        </div>
      ))}
    </div>
  )
}

/** Conversion funnel page: four-step UV, per-step rates, day/week rows (design §8). */
export default function FunnelPage() {
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().add(-13, 'day'), dayjs().add(-1, 'day')])
  const [groupBy, setGroupBy] = useState('client')
  const [grain, setGrain] = useState('day')
  const [client, setClient] = useState<string | undefined>()
  const [channel, setChannel] = useState<string | undefined>()
  const [report, setReport] = useState<FunnelReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    const params = new URLSearchParams({
      from: range[0].format('YYYY-MM-DD'),
      to: range[1].format('YYYY-MM-DD'),
      groupBy,
      grain,
    })
    if (client) params.set('client', client)
    if (channel) params.set('channel', channel)
    setLoading(true)
    setError('')
    try {
      setReport(await request<FunnelReport>(`/api/admin/funnel?${params.toString()}`))
    } catch (e) {
      setReport(null)
      setError(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [range, groupBy, grain, client, channel])

  useEffect(() => {
    void load()
  }, [load])

  const selected = report?.groups[0]

  return (
    <PageContainer content="浏览 → 加购(下单意图) → 下单 → 支付的四步转化。按第一步发生日归行,当日只出对账量。">
      <ProCard style={{ marginBottom: 16 }}>
        <Space wrap>
          <DatePicker.RangePicker
            value={range}
            onChange={(value) => value && value[0] && value[1] && setRange([value[0], value[1]])}
            allowClear={false}
          />
          <Select
            value={groupBy}
            style={{ width: 140 }}
            onChange={setGroupBy}
            options={[
              { value: 'none', label: '不分端/渠道' },
              { value: 'client', label: '按端分组' },
              { value: 'channel', label: '按渠道分组' },
            ]}
          />
          <Select
            value={grain}
            style={{ width: 110 }}
            onChange={setGrain}
            options={[
              { value: 'day', label: '按日' },
              { value: 'week', label: '按周' },
            ]}
          />
          <Select
            allowClear
            placeholder="全部端"
            style={{ width: 120 }}
            value={client}
            onChange={setClient}
            options={['web', 'miniapp', 'mobile'].map((c) => ({ value: c, label: c }))}
          />
          <Select
            allowClear
            showSearch
            placeholder="全部渠道"
            style={{ width: 140 }}
            value={channel}
            onChange={setChannel}
            options={['organic', 'promo'].map((c) => ({ value: c, label: c }))}
          />
          {report && (
            <Typography.Text type="secondary">
              区间 {report.from} ~ {report.to} · 今日已收 {report.receivedToday.toLocaleString()} 条事件
            </Typography.Text>
          )}
        </Space>
      </ProCard>

      <Row gutter={16}>
        <Col span={24} md={12}>
          <ProCard title="漏斗总览" style={{ marginBottom: 16 }}>
            {loading ? (
              <PageLoading />
            ) : error ? (
              <ErrorAlert description={error} onRetry={load} />
            ) : selected ? (
              <FunnelBars steps={[...selected.steps].sort((a, b) => STEP_NAMES.indexOf(a.step) - STEP_NAMES.indexOf(b.step))} />
            ) : (
              <EmptyAction
                description="暂无聚合数据(数据每日 00:30 UTC 出 T-1)"
                actionLabel="清空筛选"
                onAction={() => {
                  setClient(undefined)
                  setChannel(undefined)
                }}
              />
            )}
          </ProCard>
        </Col>
        <Col span={24} md={12}>
          <ProCard title="步骤明细" style={{ marginBottom: 16 }}>
            {loading ? (
              <PageLoading rows={3} />
            ) : (
            <Table<FunnelStep>
              rowKey="step"
              size="small"
              pagination={false}
              dataSource={selected?.steps ?? []}
              columns={[
                { title: '步骤', dataIndex: 'label' },
                { title: 'UV', dataIndex: 'uv', width: 100, render: (v) => v.toLocaleString() },
                { title: '转化率', dataIndex: 'rate', width: 100, render: (v: number) => (v === 0 ? '—' : `${(v * 100).toFixed(1)}%`) },
                { title: '流失率', dataIndex: 'dropRate', width: 100, render: (v: number) => (v === 0 ? '—' : `${(v * 100).toFixed(1)}%`) },
              ]}
            />
            )}
          </ProCard>
        </Col>
      </Row>

      <ProCard title="分组明细">
        <Table<FunnelRow>
          rowKey={(r) => `${r.date}-${r.client}-${r.channel}`}
          size="small"
          pagination={{ pageSize: 14, showSizeChanger: false }}
          dataSource={report?.rows ?? []}
          loading={loading}
          columns={[
            { title: '日期', dataIndex: 'date', width: 110, sorter: (a, b) => a.date.localeCompare(b.date) },
            { title: '端', dataIndex: 'client', width: 100 },
            { title: '渠道', dataIndex: 'channel', width: 110 },
            { title: '浏览商品', dataIndex: 'viewProductUv', render: (v) => v.toLocaleString() },
            { title: '加购', dataIndex: 'checkoutIntentUv', render: (v) => v.toLocaleString() },
            { title: '下单', dataIndex: 'orderCreatedUv', render: (v) => v.toLocaleString() },
            { title: '支付', dataIndex: 'orderPaidUv', render: (v) => v.toLocaleString() },
          ]}
        />
      </ProCard>
    </PageContainer>
  )
}
