import { PageContainer, ProCard, StatisticCard } from '@ant-design/pro-components'
import { request, useModel } from '@umijs/max'
import { useEffect, useState } from 'react'

interface Stats {
  users: number
  orders: number
  paidOrders: number
  revenueCents: number
  newUsersToday: number
}

export default function DashboardPage() {
  const { initialState } = useModel('@@initialState')
  const [stats, setStats] = useState<Stats | null>(null)

  useEffect(() => {
    request<Stats>('/api/admin/stats').then(setStats).catch(() => {})
  }, [])

  return (
    <PageContainer content={`欢迎, ${initialState?.nickname ?? '管理员'}`}>
      <ProCard>
        <StatisticCard.Group>
          <StatisticCard statistic={{ title: '用户总数', value: stats?.users ?? '-' }} />
          <StatisticCard statistic={{ title: '今日新增用户', value: stats?.newUsersToday ?? '-' }} />
          <StatisticCard statistic={{ title: '订单总数', value: stats?.orders ?? '-' }} />
          <StatisticCard statistic={{ title: '有效订单', value: stats?.paidOrders ?? '-' }} />
          <StatisticCard
            statistic={{ title: '订单金额 (元)', value: stats ? (stats.revenueCents / 100).toFixed(2) : '-' }}
          />
        </StatisticCard.Group>
      </ProCard>
    </PageContainer>
  )
}
