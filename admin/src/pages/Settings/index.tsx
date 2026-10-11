import { PageContainer, ProCard } from '@ant-design/pro-components'
import { useModel } from '@umijs/max'
import { Typography } from 'antd'

/** Placeholder settings page: deployment-level config lives in env, not in the DB. */
export default function SettingsPage() {
  const { initialState } = useModel('@@initialState')
  return (
    <PageContainer>
      <ProCard title="当前账号">
        <Typography.Paragraph>
          登录账号:{initialState?.nickname ?? '-'}（管理员）
        </Typography.Paragraph>
        <Typography.Paragraph type="secondary">
          系统配置（支付、Steam、金币规则等）通过服务端环境变量下发,不提供在线修改入口,避免误操作影响线上行为。
        </Typography.Paragraph>
      </ProCard>
    </PageContainer>
  )
}
