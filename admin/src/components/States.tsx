import { Alert, Button, Empty, Skeleton } from 'antd'

/** Page-level loading: skeleton block instead of '-' placeholders or bare text. */
export function PageLoading({ rows = 4 }: { rows?: number }) {
  return <Skeleton active paragraph={{ rows }} title={false} style={{ padding: '16px 24px' }} />
}

/** Error box: what failed plus a retry button. */
export function ErrorAlert({ description, retryLabel = '重试', onRetry }: { description: string; retryLabel?: string; onRetry: () => void }) {
  return (
    <div style={{ padding: '0 24px' }}>
      <Alert type="error" showIcon message="加载失败" description={description} />
      <div style={{ textAlign: 'center', padding: '8px 0 16px' }}>
        <Button onClick={onRetry}>{retryLabel}</Button>
      </div>
    </div>
  )
}

/** Empty box: reason plus the next action (e.g. clear filters / go create). */
export function EmptyAction({ description, actionLabel, onAction }: { description: string; actionLabel: string; onAction: () => void }) {
  return (
    <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={description}>
      <Button onClick={onAction}>{actionLabel}</Button>
    </Empty>
  )
}
