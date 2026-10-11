import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { Switch, Tag, message } from 'antd'
import { useRef } from 'react'

interface TemplateRow {
  id: string
  title: string
  ruleType: number
  durationMinutes: number
  entryDeadlineMinutes: number
  maxPrizeValueCents: number
  enabled: boolean
}

const RULE_LABEL: Record<number, string> = {
  1: '登录参与',
  2: '购买参与',
  3: '礼包赠送',
  4: '固定邀请码',
  5: '全蓝免费',
}

/** Roll room templates: enable or disable each rule. */
export default function RollsPage() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<TemplateRow>[] = [
    { title: '名称', dataIndex: 'title' },
    {
      title: '规则',
      dataIndex: 'ruleType',
      width: 120,
      render: (_, row) => <Tag>{RULE_LABEL[row.ruleType] ?? row.ruleType}</Tag>,
    },
    { title: '时长 (分)', dataIndex: 'durationMinutes', width: 90 },
    { title: '截止 (分)', dataIndex: 'entryDeadlineMinutes', width: 90 },
    {
      title: '奖品上限 (元)',
      dataIndex: 'maxPrizeValueCents',
      width: 110,
      render: (v) => (Number(v) / 100).toFixed(2),
    },
    {
      title: '启用',
      dataIndex: 'enabled',
      width: 80,
      render: (_, row) => (
        <Switch
          checked={row.enabled}
          onChange={async (checked) => {
            await request(`/api/admin/roll/templates/${row.id}/enabled`, { method: 'PUT', data: { enabled: checked } })
            message.success(checked ? '已启用' : '已停用')
            reload()
          }}
        />
      ),
    },
  ]

  return (
    <PageContainer>
      <ProTable<TemplateRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async () => {
          const rows = await request<TemplateRow[]>('/api/roll/templates').catch(() => [])
          return { data: rows, total: rows.length, success: true }
        }}
        search={false}
      />
    </PageContainer>
  )
}
