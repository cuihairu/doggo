import {
  ModalForm,
  PageContainer,
  ProFormDateTimePicker,
  ProFormText,
  ProFormTextArea,
  ProTable,
  type ActionType,
  type ProColumns,
} from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useEffect, useRef, useState } from 'react'
import { Button, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Switch, Tabs, Tag, message } from 'antd'
import { ErrorAlert } from '../../components/States'
import dayjs, { type Dayjs } from 'dayjs'
import utc from 'dayjs/plugin/utc'

dayjs.extend(utc)

interface ArticleRow {
  id: string
  title: string
  summary?: string | null
  category: string
  keywords: string
  viewCount: number
  createdAtUtc: string
}

interface SourceRow {
  id: string
  name: string
  sourceType: number
  game: number
  url: string
  fetchIntervalMinutes: number
  enabled: boolean
  lastFetchedAtUtc?: string | null
  consecutiveFailures: number
}

interface NewsItemRow {
  id: string
  title: string
  category: string
  sourceId: string
  url: string
  publishedAtUtc: string
}

interface NoticeRow {
  id: string
  title: string
  content: string
  enabled: boolean
  startAtUtc?: string | null
  endAtUtc?: string | null
}

interface BannerRow {
  id: string
  title: string
  imageUrl: string
  linkUrl: string
  position: string
  sortOrder: number
  enabled: boolean
  startAtUtc?: string | null
  endAtUtc?: string | null
}

interface ProblemBody {
  detail?: string
  title?: string
}

const fmt = (v: unknown) => String(v ?? '').slice(0, 19).replace('T', ' ')

/** Server validation text arrives as ProblemDetails; prefer it over a generic message. */
function serverMessage(e: unknown, fallback: string): string {
  const err = e as { data?: ProblemBody; response?: { data?: ProblemBody } }
  const body = err?.data ?? err?.response?.data
  return body?.detail ?? body?.title ?? (e instanceof Error ? e.message : fallback)
}

/** Delivery windows are UTC; ProFormDateTimePicker yields formatted wall-clock strings. */
function toIso(value?: string | Dayjs | null): string | null {
  if (!value) return null
  if (typeof value === 'string') return new Date(value.replace(' ', 'T') + 'Z').toISOString()
  return value.toISOString()
}

function ArticleTable() {
  const actionRef = useRef<ActionType>()
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<ArticleRow>[] = [
    { title: '标题', dataIndex: 'title' },
    { title: '分类', dataIndex: 'category', width: 100 },
    { title: '关键词', dataIndex: 'keywords', width: 160 },
    { title: '浏览', dataIndex: 'viewCount', width: 80 },
    { title: '创建时间', dataIndex: 'createdAtUtc', width: 160, render: fmt },
    {
      title: '操作',
      valueType: 'option',
      width: 140,
      render: (_, row) => [
        <Popconfirm
          key="del"
          title="确认删除"
          onConfirm={async () => {
            await request(`/api/admin/articles/${row.id}`, { method: 'DELETE' })
            message.success('已删除')
            reload()
          }}
        >
          <Button size="small" danger>
            删除
          </Button>
        </Popconfirm>,
      ],
    },
  ]

  return (
    <>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<ArticleRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        try {
          const rows = await request<ArticleRow[]>('/api/admin/articles')
          setError('')
          return { data: rows, total: rows.length, success: true }
        } catch (e) {
          setError(e instanceof Error ? e.message : '加载失败')
          return { data: [], total: 0, success: true }
        }
      }}
      search={false}
      toolBarRender={() => [
        <ModalForm
          key="create"
          title="新建文章"
          trigger={<Button type="primary">新建文章</Button>}
          onFinish={async (values) => {
            await request('/api/admin/articles', { method: 'POST', data: values })
            message.success('已创建')
            reload()
            return true
          }}
        >
          <ProFormText name="title" label="标题" rules={[{ required: true }]} />
          <ProFormText name="category" label="分类" />
          <ProFormText name="keywords" label="关键词" />
          <ProFormTextArea name="summary" label="摘要" />
          <ProFormTextArea name="contentHtml" label="正文 HTML" rules={[{ required: true }]} />
        </ModalForm>,
      ]}
    />
    </>
  )
}

function SourceTable() {
  const actionRef = useRef<ActionType>()
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<SourceRow>[] = [
    { title: '名称', dataIndex: 'name', width: 180 },
    { title: '游戏', dataIndex: 'game', width: 80, render: (v) => (Number(v) === 1 ? 'Dota2' : 'CS2') },
    { title: '地址', dataIndex: 'url', ellipsis: true },
    { title: '抓取间隔(分)', dataIndex: 'fetchIntervalMinutes', width: 110 },
    {
      title: '最近抓取',
      dataIndex: 'lastFetchedAtUtc',
      width: 150,
      render: (v) => (v ? fmt(v) : '-'),
    },
    {
      title: '连续失败',
      dataIndex: 'consecutiveFailures',
      width: 90,
      render: (v) => (Number(v) > 0 ? <Tag color="red">{String(v)}</Tag> : <Tag color="green">0</Tag>),
    },
    {
      title: '启用',
      dataIndex: 'enabled',
      width: 80,
      render: (_, row) => (
        <Switch
          checked={row.enabled}
          onChange={async (checked) => {
            await request(`/api/admin/news-sources/${row.id}/enabled`, { method: 'PUT', data: { enabled: checked } })
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
      <ProTable<SourceRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        try {
          const rows = await request<SourceRow[]>('/api/admin/news-sources')
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

function NewsItemTable() {
  const actionRef = useRef<ActionType>()
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<NewsItemRow>[] = [
    { title: '标题', dataIndex: 'title', ellipsis: true },
    { title: '分类', dataIndex: 'category', width: 110 },
    { title: '发布时间', dataIndex: 'publishedAtUtc', width: 160, render: fmt },
  ]

  return (
    <>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<NewsItemRow>
      rowKey="id"
      columns={columns}
      request={async (params) => {
        try {
          const data = await request<{ total: number; items: NewsItemRow[] }>('/api/admin/news-items', {
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
    </>
  )
}

function NoticeTable() {
  const actionRef = useRef<ActionType>()
  const [error, setError] = useState('')
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<NoticeRow>[] = [
    { title: '标题', dataIndex: 'title' },
    { title: '内容', dataIndex: 'content', ellipsis: true },
    {
      title: '状态',
      dataIndex: 'enabled',
      width: 80,
      render: (_, row) => (row.enabled ? <Tag color="green">生效</Tag> : <Tag>停用</Tag>),
    },
    { title: '开始', dataIndex: 'startAtUtc', width: 150, render: (v) => (v ? fmt(v) : '-') },
    { title: '结束', dataIndex: 'endAtUtc', width: 150, render: (v) => (v ? fmt(v) : '-') },
    {
      title: '操作',
      valueType: 'option',
      width: 100,
      render: (_, row) => [
        <Popconfirm
          key="del"
          title="确认删除"
          onConfirm={async () => {
            await request(`/api/admin/notices/${row.id}`, { method: 'DELETE' })
            message.success('已删除')
            reload()
          }}
        >
          <Button size="small" danger>
            删除
          </Button>
        </Popconfirm>,
      ],
    },
  ]

  return (
    <>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<NoticeRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        try {
          const rows = await request<NoticeRow[]>('/api/admin/notices')
          setError('')
          return { data: rows, total: rows.length, success: true }
        } catch (e) {
          setError(e instanceof Error ? e.message : '加载失败')
          return { data: [], total: 0, success: true }
        }
      }}
      search={false}
      toolBarRender={() => [
        <ModalForm
          key="create"
          title="发布公告"
          trigger={<Button type="primary">发布公告</Button>}
          onFinish={async (values) => {
            await request('/api/admin/notices', { method: 'POST', data: values })
            message.success('已发布')
            reload()
            return true
          }}
        >
          <ProFormText name="title" label="标题" rules={[{ required: true }]} />
          <ProFormTextArea name="content" label="内容" rules={[{ required: true }]} />
        </ModalForm>,
      ]}
    />
    </>
  )
}

/** Banner ops: list, create, edit with delivery windows (same-position overlap is rejected server-side). */
function BannerTable() {
  const actionRef = useRef<ActionType>()
  const [error, setError] = useState('')
  const [editing, setEditing] = useState<BannerRow | null>(null)
  const [saving, setSaving] = useState(false)
  const [form] = Form.useForm()
  const reload = () => actionRef.current?.reload()

  useEffect(() => {
    if (editing) {
      form.setFieldsValue({
        title: editing.title,
        imageUrl: editing.imageUrl,
        linkUrl: editing.linkUrl,
        position: editing.position,
        sortOrder: editing.sortOrder,
        enabled: editing.enabled,
        startAtUtc: editing.startAtUtc ? dayjs.utc(editing.startAtUtc) : undefined,
        endAtUtc: editing.endAtUtc ? dayjs.utc(editing.endAtUtc) : undefined,
      })
    }
  }, [editing, form])

  async function save() {
    if (!editing) return
    const values = await form.validateFields()
    setSaving(true)
    try {
      await request(`/api/admin/banners/${editing.id}`, {
        method: 'PUT',
        data: {
          title: values.title as string,
          imageUrl: values.imageUrl as string,
          linkUrl: (values.linkUrl as string) ?? '',
          position: values.position as string,
          sortOrder: Number(values.sortOrder ?? 0),
          enabled: Boolean(values.enabled),
          startAtUtc: toIso(values.startAtUtc as string | Dayjs | null | undefined),
          endAtUtc: toIso(values.endAtUtc as string | Dayjs | null | undefined),
        },
      })
      message.success('已保存')
      setEditing(null)
      reload()
    } catch (e) {
      // 400 = 窗口起止非法;409 = 同位置启用横幅窗口重叠
      message.error(serverMessage(e, '保存失败'))
    } finally {
      setSaving(false)
    }
  }

  const columns: ProColumns<BannerRow>[] = [
    { title: '标题', dataIndex: 'title' },
    { title: '图片', dataIndex: 'imageUrl', ellipsis: true },
    { title: '链接', dataIndex: 'linkUrl', ellipsis: true },
    { title: '位置', dataIndex: 'position', width: 100 },
    { title: '排序', dataIndex: 'sortOrder', width: 70 },
    {
      title: '投放窗口 (UTC)',
      width: 230,
      render: (_, row) => (
        <span style={{ fontSize: 12 }}>
          {row.startAtUtc ? fmt(row.startAtUtc) : '不限'} ~ {row.endAtUtc ? fmt(row.endAtUtc) : '不限'}
        </span>
      ),
    },
    {
      title: '状态',
      dataIndex: 'enabled',
      width: 80,
      render: (_, row) => (row.enabled ? <Tag color="green">展示中</Tag> : <Tag>已下线</Tag>),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 130,
      render: (_, row) => [
        <Button key="edit" size="small" type="link" onClick={() => setEditing(row)}>
          编辑
        </Button>,
        <Popconfirm
          key="del"
          title="下线该 Banner？"
          onConfirm={async () => {
            await request(`/api/admin/banners/${row.id}`, { method: 'DELETE' })
            message.success('已下线')
            reload()
          }}
        >
          <Button size="small" danger>
            下线
          </Button>
        </Popconfirm>,
      ],
    },
  ]

  return (
    <>
      {error && <ErrorAlert description={error} onRetry={() => { setError(''); reload() }} />}
      <ProTable<BannerRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        try {
          const rows = await request<BannerRow[]>('/api/admin/banners')
          setError('')
          return { data: rows, total: rows.length, success: true }
        } catch (e) {
          setError(e instanceof Error ? e.message : '加载失败')
          return { data: [], total: 0, success: true }
        }
      }}
      search={false}
      toolBarRender={() => [
        <ModalForm
          key="create"
          title="新建 Banner"
          trigger={<Button type="primary">新建 Banner</Button>}
          onFinish={async (values) => {
            try {
              await request('/api/admin/banners', {
                method: 'POST',
                data: {
                  title: values.title,
                  imageUrl: values.imageUrl,
                  linkUrl: values.linkUrl ?? '',
                  position: values.position,
                  sortOrder: Number(values.sortOrder ?? 0),
                  startAtUtc: toIso(values.startAtUtc as string | Dayjs | null | undefined),
                  endAtUtc: toIso(values.endAtUtc as string | Dayjs | null | undefined),
                },
              })
              message.success('已创建')
              reload()
              return true
            } catch (e) {
              message.error(serverMessage(e, '创建失败'))
              return false
            }
          }}
        >
          <ProFormText name="title" label="标题" rules={[{ required: true }]} />
          <ProFormText name="imageUrl" label="图片地址" rules={[{ required: true }]} />
          <ProFormText name="linkUrl" label="跳转地址" />
          <ProFormText name="position" label="位置" placeholder="home" />
          <ProFormText name="sortOrder" label="排序" />
          <ProFormDateTimePicker name="startAtUtc" label="投放开始 (UTC,可空)" fieldProps={{ showTime: true, format: 'YYYY-MM-DD HH:mm' }} />
          <ProFormDateTimePicker name="endAtUtc" label="投放结束 (UTC,可空)" fieldProps={{ showTime: true, format: 'YYYY-MM-DD HH:mm' }} />
        </ModalForm>,
      ]}
    />

      <Modal
        title={editing ? `编辑 Banner:${editing.title}` : ''}
        open={editing !== null}
        onOk={save}
        okText="保存"
        confirmLoading={saving}
        onCancel={() => setEditing(null)}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="title" label="标题" rules={[{ required: true, message: '请输入标题' }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="imageUrl" label="图片地址" rules={[{ required: true, message: '请输入图片地址' }]}>
            <Input />
          </Form.Item>
          <Form.Item name="linkUrl" label="跳转地址">
            <Input />
          </Form.Item>
          <Form.Item name="position" label="位置">
            <Input maxLength={50} placeholder="home" />
          </Form.Item>
          <Form.Item name="sortOrder" label="排序">
            <InputNumber min={0} precision={0} style={{ width: 120 }} />
          </Form.Item>
          <Form.Item name="startAtUtc" label="投放开始 (UTC)">
            <DatePicker showTime={{ format: 'HH:mm' }} format="YYYY-MM-DD HH:mm" />
          </Form.Item>
          <Form.Item name="endAtUtc" label="投放结束 (UTC)">
            <DatePicker showTime={{ format: 'HH:mm' }} format="YYYY-MM-DD HH:mm" />
          </Form.Item>
          <Form.Item name="enabled" label="启用" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}

/** Content center: scraper sources, fetched items, articles, notices, banners. */
export default function NewsCenterPage() {
  return (
    <PageContainer>
      <Tabs
        defaultActiveKey="articles"
        items={[
          { key: 'articles', label: '文章', children: <ArticleTable /> },
          { key: 'sources', label: '资讯源', children: <SourceTable /> },
          { key: 'items', label: '资讯条目', children: <NewsItemTable /> },
          { key: 'notices', label: '公告', children: <NoticeTable /> },
          { key: 'banners', label: 'Banner', children: <BannerTable /> },
        ]}
      />
    </PageContainer>
  )
}
