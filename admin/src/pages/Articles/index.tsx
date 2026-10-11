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
import { Button, Popconfirm, message } from 'antd'

interface ArticleRow {
  id: string
  title: string
  summary?: string | null
  category: string
  keywords: string
  viewCount: number
  createdAtUtc: string
}

const columns: ProColumns<ArticleRow>[] = [
  { title: '标题', dataIndex: 'title' },
  { title: '分类', dataIndex: 'category', width: 100 },
  { title: '关键词', dataIndex: 'keywords', width: 160 },
  { title: '浏览', dataIndex: 'viewCount', width: 80 },
  { title: '创建时间', dataIndex: 'createdAtUtc', width: 170, render: (value) => String(value).slice(0, 19).replace('T', ' ') },
  {
    title: '操作',
    valueType: 'option',
    width: 140,
    render: (_, row) => [<DeleteArticleButton key="del" id={row.id} onDone={reload} />],
  },
]

/** Article management: create and delete operator content. */
export default function ArticlesPage() {
  const actionRef = useRef<ActionType>()
  const reload = () => actionRef.current?.reload()
  return (
    <PageContainer>
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
    </PageContainer>
  )
}

export function DeleteArticleButton({ id, onDone }: { id: string; onDone?: () => void }) {
  return (
    <Popconfirm
      title="确认删除"
      onConfirm={async () => {
        await request(`/api/admin/articles/${id}`, { method: 'DELETE' })
        message.success('已删除')
        onDone?.()
      }}
    >
      <Button size="small" danger>
        删除
      </Button>
    </Popconfirm>
  )
}
