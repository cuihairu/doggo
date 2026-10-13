import { Button, Text, View } from '@tarojs/components'
import type { ReactNode } from 'react'

/** Shared page states (loading / empty / error / success), ui-system.md §6.
 * Weapp has no SVG components, so the badge is an outlined circle with a text
 * glyph; box/warn/check variants color themselves from theme tokens. */

const GLYPH = { box: '▣', warn: '!', check: '✓' } as const

function StateBadge({ tone }: { tone: 'box' | 'warn' | 'check' }) {
  return (
    <View className="state-badge">
      <Text>{GLYPH[tone]}</Text>
    </View>
  )
}

/** Loading skeleton: pulsing placeholder cards instead of "加载中…" text. */
export function Skeleton({ cards = 3 }: { cards?: number }) {
  return (
    <>
      {Array.from({ length: cards }, (_, index) => (
        <View key={index} className="skel-card">
          <View className="skel-line" style={{ width: '60%' }} />
          <View className="skel-line" style={{ width: '35%' }} />
        </View>
      ))}
    </>
  )
}

/** Empty state: says why nothing is shown and offers the next action. */
export function EmptyState({
  title,
  hint,
  actionLabel,
  onAction
}: {
  title: string
  hint?: string
  actionLabel?: string
  onAction?: () => void
}) {
  return (
    <View className="card">
      <View className="state-box">
        <StateBadge tone="box" />
        <Text className="state-title">{title}</Text>
        {hint ? <Text className="state-hint">{hint}</Text> : null}
        {actionLabel && onAction ? (
          <Button size="mini" className="btn-ghost" onClick={onAction}>
            {actionLabel}
          </Button>
        ) : null}
      </View>
    </View>
  )
}

/** Error state: warn badge + reason + retry; `children` renders an alternative path. */
export function ErrorBox({
  text,
  onRetry,
  hint,
  children
}: {
  text: string
  onRetry?: () => void
  hint?: string
  children?: ReactNode
}) {
  return (
    <View className="card">
      <View className="state-box error-box">
        <StateBadge tone="warn" />
        <Text className="state-title">{text}</Text>
        {hint ? <Text className="state-hint">{hint}</Text> : null}
        {onRetry ? (
          <Button size="mini" className="btn-ghost" onClick={onRetry}>
            重试
          </Button>
        ) : null}
        {children}
      </View>
    </View>
  )
}

/** Success state: what happened and where to go next. */
export function SuccessBox({
  title,
  detail,
  actionLabel,
  onAction
}: {
  title: string
  detail?: string
  actionLabel?: string
  onAction?: () => void
}) {
  return (
    <View className="card">
      <View className="state-box success-box">
        <StateBadge tone="check" />
        <Text className="state-title">{title}</Text>
        {detail ? <Text className="state-hint">{detail}</Text> : null}
        {actionLabel && onAction ? (
          <Button size="mini" className="btn-primary" onClick={onAction}>
            {actionLabel}
          </Button>
        ) : null}
      </View>
    </View>
  )
}
