import {
  ModalForm,
  PageContainer,
  ProFormText,
  ProFormTextArea,
  ProTable,
  type ActionType,
  type ProColumns,
} from '@ant-design/pro-components'
import { request } from '@umijs/max'
import { useRef } from 'react'
import { Button, Popconfirm, Switch, Tabs, Tag, message } from 'antd'

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
}

const fmt = (v: unknown) => String(v ?? '').slice(0, 19).replace('T', ' ')

function ArticleTable() {
  const actionRef = useRef<ActionType>()
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
    <ProTable<ArticleRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        const rows = await request<ArticleRow[]>('/api/admin/articles').catch(() => [])
        return { data: rows, total: rows.length, success: true }
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
  )
}

function SourceTable() {
  const actionRef = useRef<ActionType>()
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
    <ProTable<SourceRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        const rows = await request<SourceRow[]>('/api/admin/news-sources').catch(() => [])
        return { data: rows, total: rows.length, success: true }
      }}
      search={false}
    />
  )
}

function NewsItemTable() {
  const columns: ProColumns<NewsItemRow>[] = [
    { title: '标题', dataIndex: 'title', ellipsis: true },
    { title: '分类', dataIndex: 'category', width: 110 },
    { title: '发布时间', dataIndex: 'publishedAtUtc', width: 160, render: fmt },
  ]

  return (
    <ProTable<NewsItemRow>
      rowKey="id"
      columns={columns}
      request={async (params) => {
        const data = await request<{ total: number; items: NewsItemRow[] }>('/api/admin/news-items', {
          method: 'GET',
          params: { page: params.current ?? 1, pageSize: params.pageSize ?? 20 },
        }).catch(() => ({ total: 0, items: [] }))
        return { data: data.items, total: data.total, success: true }
      }}
      search={false}
      pagination={{ pageSize: 20 }}
    />
  )
}

function NoticeTable() {
  const actionRef = useRef<ActionType>()
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
    <ProTable<NoticeRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        const rows = await request<NoticeRow[]>('/api/admin/notices').catch(() => [])
        return { data: rows, total: rows.length, success: true }
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
  )
}

function BannerTable() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()

  const columns: ProColumns<BannerRow>[] = [
    { title: '标题', dataIndex: 'title' },
    { title: '图片', dataIndex: 'imageUrl', ellipsis: true },
    { title: '链接', dataIndex: 'linkUrl', ellipsis: true },
    { title: '位置', dataIndex: 'position', width: 100 },
    { title: '排序', dataIndex: 'sortOrder', width: 70 },
    {
      title: '状态',
      dataIndex: 'enabled',
      width: 80,
      render: (_, row) => (row.enabled ? <Tag color="green">展示中</Tag> : <Tag>已下线</Tag>),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 100,
      render: (_, row) => [
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
    <ProTable<BannerRow>
      rowKey="id"
      actionRef={actionRef}
      columns={columns}
      request={async () => {
        const rows = await request<BannerRow[]>('/api/admin/banners').catch(() => [])
        return { data: rows, total: rows.length, success: true }
      }}
      search={false}
      toolBarRender={() => [
        <ModalForm
          key="create"
          title="新建 Banner"
          trigger={<Button type="primary">新建 Banner</Button>}
          onFinish={async (values) => {
            await request('/api/admin/banners', { method: 'POST', data: { ...values, sortOrder: Number(values.sortOrder ?? 0) } })
            message.success('已创建')
            reload()
            return true
          }}
        >
          <ProFormText name="title" label="标题" rules={[{ required: true }]} />
          <ProFormText name="imageUrl" label="图片地址" rules={[{ required: true }]} />
          <ProFormText name="linkUrl" label="跳转地址" />
          <ProFormText name="position" label="位置" placeholder="home" />
          <ProFormText name="sortOrder" label="排序" />
        </ModalForm>,
      ]}
    />
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
