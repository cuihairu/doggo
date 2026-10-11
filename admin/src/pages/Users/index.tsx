import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { Switch, Tag, message } from 'antd'
import { useRef } from 'react'

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

/** User management: paged list, disable/enable toggle, admin flag display. */
export default function UsersPage() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<UserRow>[] = [
    { title: '昵称', dataIndex: 'nickname', width: 140 },
    { title: '手机号', dataIndex: 'phone', width: 130 },
    { title: '邮箱', dataIndex: 'email', width: 180 },
    { title: '邀请码', dataIndex: 'inviteCode', width: 100 },
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
    {
      title: '注册时间',
      dataIndex: 'createdAtUtc',
      width: 160,
      render: (v) => String(v).slice(0, 19).replace('T', ' '),
    },
    {
      title: '禁用',
      width: 80,
      render: (_, row) => (
        <Switch
          checked={row.status === 2}
          onChange={async (checked) => {
            await request(`/api/admin/users/${row.id}/status`, { method: 'PUT', data: { disabled: checked } })
            message.success(checked ? '已禁用' : '已启用')
            reload()
          }}
        />
      ),
    },
  ]

  return (
    <PageContainer>
      <ProTable<UserRow>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        request={async (params) => {
          const page = params.current ?? 1
          const pageSize = params.pageSize ?? 20
          const data = await request<{ total: number; items: UserRow[] }>('/api/admin/users', {
            method: 'GET',
            params: { page, pageSize },
          }).catch(() => ({ total: 0, items: [] }))
          return { data: data.items, total: data.total, success: true }
        }}
        search={false}
        pagination={{ pageSize: 20 }}
      />
    </PageContainer>
  )
}
