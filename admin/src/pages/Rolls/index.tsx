import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useRef, useState } from 'react'
import { Button, Modal, Switch, Tabs, Tag, message } from 'antd'
import { ErrorAlert } from '../../components/States'

interface TemplateRow {
  id: string
  title: string
  ruleType: number
  durationMinutes: number
  entryDeadlineMinutes: number
  maxPrizeValueCents: number
  enabled: boolean
}

interface RoomRow {
  id: string
  title: string
  ruleType: number
  status: number
  inviteCode?: string | null
  maxPrizeValueCents: number
  startAtUtc?: string | null
  endAtUtc?: string | null
}

interface RoomPrizeRow {
  id: string
  type: number
  title: string
  coinAmount: number
  weight: number
  imageUrl: string
}

const RULE_LABEL: Record<number, string> = {
  1: '登录参与',
  2: '购买参与',
  3: '礼包赠送',
  4: '固定邀请码',
  5: '全蓝免费',
}

const ROOM_STATUS: Record<number, { text: string; color: string }> = {
  0: { text: '创建中', color: 'default' },
  1: { text: '进行中', color: 'blue' },
  2: { text: '已开奖', color: 'green' },
  3: { text: '已关闭', color: 'default' },
}

const fmt = (v: unknown) => (v ? String(v).slice(0, 19).replace('T', ' ') : '-')

function TemplateTable() {
  const actionRef = useRef<ActionType>()
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<TemplateRow>[] = [
    { title: '名称', dataIndex: 'title' },
    { title: '规则', dataIndex: 'ruleType', width: 110, render: (_, row) => <Tag>{RULE_LABEL[row.ruleType] ?? row.ruleType}</Tag> },
    { title: '时长 (分)', dataIndex: 'durationMinutes', width: 90 },
    { title: '截止 (分)', dataIndex: 'entryDeadlineMinutes', width: 90 },
    { title: '奖品上限 (元)', dataIndex: 'maxPrizeValueCents', width: 110, render: (v) => (Number(v) / 100).toFixed(2) },
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
    <>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<TemplateRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        try {
          const rows = await request<TemplateRow[]>('/api/roll/templates')
          setError('')
          return { data: rows, total: rows.length, success: true }
        } catch (e) {
          setError(e instanceof Error ? e.message : '加载失败')
          return { data: [], total: 0, success: true }
        }
      }}
      search={false}
    />
    </>
  )
}

function RoomTable() {
  const actionRef = useRef<ActionType>()
  const [prizeRoom, setPrizeRoom] = useState<RoomRow | null>(null)
  const [prizes, setPrizes] = useState<RoomPrizeRow[]>([])
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  async function openPrizes(row: RoomRow) {
    setPrizeRoom(row)
    const rows = await request<RoomPrizeRow[]>(`/api/admin/roll/rooms/${row.id}/prizes`).catch(() => [])
    setPrizes(rows)
  }

  const columns: ProColumns<RoomRow>[] = [
    { title: '名称', dataIndex: 'title' },
    { title: '规则', dataIndex: 'ruleType', width: 110, render: (_, row) => <Tag>{RULE_LABEL[row.ruleType] ?? row.ruleType}</Tag> },
    {
      title: '状态',
      dataIndex: 'status',
      width: 90,
      render: (_, row) => {
        const s = ROOM_STATUS[row.status] ?? { text: String(row.status), color: 'default' }
        return <Tag color={s.color}>{s.text}</Tag>
      },
    },
    { title: '邀请码', dataIndex: 'inviteCode', width: 100, render: (v) => v ?? '-' },
    { title: '开始', dataIndex: 'startAtUtc', width: 150, render: fmt },
    { title: '结束', dataIndex: 'endAtUtc', width: 150, render: fmt },
    {
      title: '操作',
      valueType: 'option',
      width: 90,
      render: (_, row) => [
        <Button key="prizes" size="small" onClick={() => openPrizes(row)}>
          奖品
        </Button>,
      ],
    },
  ]

  return (
    <>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<RoomRow>
        rowKey="id"
        columns={columns}
        request={async (params) => {
          try {
            const data = await request<{ total: number; items: RoomRow[] }>('/api/admin/roll/rooms', {
              method: 'GET',
              params: { page: params.current ?? 1, pageSize: params.pageSize ?? 20 },
            })
            setError('')
            return { data: data.items, total: data.total, success: true }
          } catch (e) {
            setError(e instanceof Error ? e.message : '加载失败')
            return { data: [], total: 0, success: true }
          }
        }}
        search={false}
        pagination={{ pageSize: 20 }}
      />
      <Modal
        title={prizeRoom ? `奖品池：${prizeRoom.title}` : ''}
        open={prizeRoom !== null}
        footer={null}
        onCancel={() => setPrizeRoom(null)}
      >
        {prizes.length === 0 ? (
          <p>该房间没有配置奖品。</p>
        ) : (
          <table style={{ width: '100%', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ textAlign: 'left' }}>奖品</th>
                <th style={{ textAlign: 'left' }}>类型</th>
                <th style={{ textAlign: 'right' }}>金币</th>
                <th style={{ textAlign: 'right' }}>权重</th>
              </tr>
            </thead>
            <tbody>
              {prizes.map((p) => (
                <tr key={p.id}>
                  <td>{p.title}</td>
                  <td>{p.type}</td>
                  <td style={{ textAlign: 'right' }}>{p.coinAmount}</td>
                  <td style={{ textAlign: 'right' }}>{p.weight}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Modal>
    </>
  )
}

/** Roll rooms: template rules and room prize pools. */
export default function RollsPage() {
  return (
    <PageContainer>
      <Tabs
        defaultActiveKey="templates"
        items={[
          { key: 'templates', label: '模板', children: <TemplateTable /> },
          { key: 'rooms', label: '房间', children: <RoomTable /> },
        ]}
      />
    </PageContainer>
  )
}
