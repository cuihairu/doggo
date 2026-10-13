import { PageContainer, ProCard, StatisticCard } from '@ant-design/pro-components'
import { request, useModel } from '@umijs/max'
import { useCallback, useEffect, useState } from 'react'
import { Table, Typography, theme as antdTheme } from 'antd'
import { ErrorAlert, PageLoading } from '../../components/States'

interface Stats {
  users: number
  orders: number
  paidOrders: number
  revenueCents: number
  newUsersToday: number
}

interface TrendPoint {
  date: string
  newUsers: number
  orders: number
  revenueCents: number
}

interface TopProduct {
  productId: string
  productName: string
  orderCount: number
  revenueCents: number
}

/** SVG polyline over the last N days; two series share the y scale (counts). */
function TrendChart({ points }: { points: TrendPoint[] }) {
  const {
    token: { colorPrimary, colorSuccess }
  } = antdTheme.useToken()
  if (points.length < 2) {
    return <Typography.Text type="secondary">数据不足，暂无走势。</Typography.Text>
  }
  const width = 720
  const height = 160
  const pad = 24
  const maxUsers = Math.max(...points.map((p) => p.newUsers), 1)
  const maxOrders = Math.max(...points.map((p) => p.orders), 1)
  const toPath = (pick: (p: TrendPoint) => number, max: number) =>
    points
      .map((p, i) => {
        const x = pad + (i * (width - pad * 2)) / (points.length - 1)
        const y = height - pad - (pick(p) / max) * (height - pad * 2)
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
      })
      .join(' ')

  return (
    <div>
      <svg width={width} height={height} style={{ maxWidth: '100%' }}>
        <path d={toPath((p) => p.newUsers, maxUsers)} fill="none" stroke={colorPrimary} strokeWidth={2} />
        <path d={toPath((p) => p.orders, maxOrders)} fill="none" stroke={colorSuccess} strokeWidth={2} />
      </svg>
      <Typography.Text style={{ color: colorPrimary }}>— 新增用户</Typography.Text>{' '}
      <Typography.Text style={{ color: colorSuccess }}>— 订单</Typography.Text>
    </div>
  )
}

/** Dashboard: counters, daily trend, revenue ranking. */
export default function DashboardPage() {
  const { initialState } = useModel('@@initialState')
  const [stats, setStats] = useState<Stats | null>(null)
  const [trend, setTrend] = useState<TrendPoint[]>([])
  const [top, setTop] = useState<TopProduct[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    Promise.all([
      request<Stats>('/api/admin/stats').then(setStats).catch(() => setError('统计接口加载失败')),
      request<TrendPoint[]>('/api/admin/stats/trend?days=14').then(setTrend).catch(() => setError('走势接口加载失败')),
      request<TopProduct[]>('/api/admin/stats/top-products?top=10').then(setTop).catch(() => setError('排行接口加载失败')),
    ]).finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  return (
    <PageContainer content={`欢迎, ${initialState?.nickname ?? '管理员'}`}>
      <ProCard style={{ marginBottom: 16 }}>
        {loading ? (
          <PageLoading />
        ) : error ? (
          <ErrorAlert description={error} onRetry={load} />
        ) : (
        <StatisticCard.Group>
          <StatisticCard statistic={{ title: '用户总数', value: stats?.users ?? '-' }} />
          <StatisticCard statistic={{ title: '今日新增用户', value: stats?.newUsersToday ?? '-' }} />
          <StatisticCard statistic={{ title: '订单总数', value: stats?.orders ?? '-' }} />
          <StatisticCard statistic={{ title: '有效订单', value: stats?.paidOrders ?? '-' }} />
          <StatisticCard
            statistic={{ title: '订单金额 (元)', value: stats ? (stats.revenueCents / 100).toFixed(2) : '-' }}
          />
        </StatisticCard.Group>
        )}
      </ProCard>

      <ProCard title="近 14 天走势" style={{ marginBottom: 16 }}>
        {loading ? <PageLoading rows={2} /> : <TrendChart points={trend} />}
      </ProCard>

      <ProCard title="商品营收排行">
        <Table<TopProduct>
          rowKey="productId"
          size="small"
          pagination={false}
          dataSource={top}
          loading={loading}
          columns={[
            { title: '商品', dataIndex: 'productName' },
            { title: '订单数', dataIndex: 'orderCount', width: 100 },
            { title: '营收 (元)', dataIndex: 'revenueCents', width: 120, render: (v) => (Number(v) / 100).toFixed(2) },
          ]}
        />
      </ProCard>
    </PageContainer>
  )
}
