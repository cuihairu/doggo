import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useRef, useState } from 'react'
import { Button, Drawer, Switch, Tabs, Tag, message } from 'antd'

interface UserRow {
  id: string
  phone: string
  email: string
  nickname: string
  inviteCode: string
  status: number
  isAdmin: boolean
  createdAtUtc: string
}

interface CoinLogRow {
  id: string
  type: number
  delta: number
  balanceAfter: number
  refType?: string | null
  note?: string | null
  createdAtUtc: string
}

interface LoginLogRow {
  id: string
  account: string
  ip: string
  userAgent: string
  success: boolean
  createdAtUtc: string
}

const fmt = (v: unknown) => String(v ?? '').slice(0, 19).replace('T', ' ')

function CoinLogs({ userId }: { userId: string }) {
  const columns: ProColumns<CoinLogRow>[] = [
    { title: '变动', dataIndex: 'delta', width: 90, render: (v) => (Number(v) > 0 ? `+${v}` : String(v)) },
    { title: '变动后', dataIndex: 'balanceAfter', width: 90 },
    { title: '类型', dataIndex: 'type', width: 70 },
    { title: '关联', dataIndex: 'refType', width: 100, render: (v) => v ?? '-' },
    { title: '备注', dataIndex: 'note', ellipsis: true, render: (v) => v ?? '-' },
    { title: '时间', dataIndex: 'createdAtUtc', width: 150, render: fmt },
  ]
  return (
    <ProTable<CoinLogRow>
      rowKey="id"
      columns={columns}
      request={async (params) => {
        const data = await request<{ total: number; items: CoinLogRow[] }>(`/api/admin/users/${userId}/coin-logs`, {
          method: 'GET',
          params: { page: params.current ?? 1, pageSize: params.pageSize ?? 10 },
        }).catch(() => ({ total: 0, items: [] }))
        return { data: data.items, total: data.total, success: true }
      }}
      search={false}
      pagination={{ pageSize: 10 }}
      size="small"
    />
  )
}

function LoginLogs({ userId }: { userId: string }) {
  const columns: ProColumns<LoginLogRow>[] = [
    { title: '账号', dataIndex: 'account', width: 130 },
    { title: 'IP', dataIndex: 'ip', width: 120 },
    { title: '结果', dataIndex: 'success', width: 70, render: (v) => (v ? <Tag color="green">成功</Tag> : <Tag color="red">失败</Tag>) },
    { title: 'UA', dataIndex: 'userAgent', ellipsis: true },
    { title: '时间', dataIndex: 'createdAtUtc', width: 150, render: fmt },
  ]
  return (
    <ProTable<LoginLogRow>
      rowKey="id"
      columns={columns}
      request={async (params) => {
        const data = await request<{ total: number; items: LoginLogRow[] }>(`/api/admin/users/${userId}/login-logs`, {
          method: 'GET',
          params: { page: params.current ?? 1, pageSize: params.pageSize ?? 10 },
        }).catch(() => ({ total: 0, items: [] }))
        return { data: data.items, total: data.total, success: true }
      }}
      search={false}
      pagination={{ pageSize: 10 }}
      size="small"
    />
  )
}

/** User management: paged accounts, disable toggle, coin ledger and login history. */
export default function UsersPage() {
  const actionRef = useRef<ActionType>()
  const [logsUser, setLogsUser] = useState<UserRow | null>(null)
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<UserRow>[] = [
    { title: '昵称', dataIndex: 'nickname', width: 130 },
    { title: '手机号', dataIndex: 'phone', width: 120 },
    { title: '邮箱', dataIndex: 'email', width: 170, ellipsis: true },
    { title: '邀请码', dataIndex: 'inviteCode', width: 90 },
    {
      title: '角色',
      dataIndex: 'isAdmin',
      width: 80,
      render: (_, row) => (row.isAdmin ? <Tag color="gold">管理员</Tag> : <Tag>用户</Tag>),
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 80,
      render: (_, row) => (row.status === 2 ? <Tag color="red">已禁用</Tag> : <Tag color="green">正常</Tag>),
    },
    { title: '注册时间', dataIndex: 'createdAtUtc', width: 150, render: fmt },
    {
      title: '操作',
      valueType: 'option',
      width: 150,
      render: (_, row) => [
        <Button key="logs" size="small" onClick={() => setLogsUser(row)}>
          流水
        </Button>,
        <Switch
          key="ban"
          checked={row.status === 2}
          onChange={async (checked) => {
            await request(`/api/admin/users/${row.id}/status`, { method: 'PUT', data: { disabled: checked } })
            message.success(checked ? '已禁用' : '已启用')
            reload()
          }}
        />,
      ],
    },
  ]

  return (
    <PageContainer>
      <ProTable<UserRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async (params) => {
          const data = await request<{ total: number; items: UserRow[] }>('/api/admin/users', {
            method: 'GET',
            params: { page: params.current ?? 1, pageSize: params.pageSize ?? 20 },
          }).catch(() => ({ total: 0, items: [] }))
          return { data: data.items, total: data.total, success: true }
        }}
        search={false}
        pagination={{ pageSize: 20 }}
      />

      <Drawer
        title={logsUser ? `${logsUser.nickname} 的流水与登录` : ''}
        width={860}
        open={logsUser !== null}
        onClose={() => setLogsUser(null)}
      >
        {logsUser && (
          <Tabs
            items={[
              { key: 'coins', label: '金币流水', children: <CoinLogs userId={logsUser.id} /> },
              { key: 'logins', label: '登录日志', children: <LoginLogs userId={logsUser.id} /> },
            ]}
          />
        )}
      </Drawer>
    </PageContainer>
  )
}
