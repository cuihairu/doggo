import type { ReactNode } from 'react'

/** Shared page states (loading / empty / error / success).
 * Design source: docs/design/prototypes/web/states.html, ui-system.md §6.
 * Icons inherit the box color via currentColor (muted / danger / success). */

const ICONS = {
  box: ['M4 8l8-4 8 4v8l-8 4-8-4V8z', 'M4 8l8 4 8-4', 'M12 12v8'],
  warn: ['M12 3L2 20h20L12 3z', 'M12 10v4', 'M12 17.2v.3'],
  check: ['M4 12.5l5 5L20 6.5'],
} as const

const REFRESH = ['M20 12a8 8 0 1 1-2.3-5.6', 'M20 3v4h-4']

type IcoName = keyof typeof ICONS

function StateIco({ name }: { name: IcoName | 'refresh' }) {
  const paths = name === 'refresh' ? REFRESH : ICONS[name]
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {paths.map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  )
}

/** Shimmer placeholder: a block plus `lines` lines (last one narrower). */
export function Skeleton({ lines = 4 }: { lines?: number }) {
  return (
    <div className="skel" aria-hidden="true">
      <div className="skeleton skel-block" />
      {Array.from({ length: lines }, (_, index) => (
        <div
          key={index}
          className="skeleton skel-line"
          style={index === lines - 1 ? { width: '50%' } : undefined}
        />
      ))}
    </div>
  )
}

/** Empty state: says why nothing is shown and offers the next action. */
export function EmptyState({
  title,
  hint,
  actionLabel,
  onAction,
}: {
  title: string
  hint?: string
  actionLabel?: string
  onAction?: () => void
}) {
  return (
    <div className="state-box empty">
      <StateIco name="box" />
      <p className="state-title">{title}</p>
      {hint && <p className="state-hint">{hint}</p>}
      {actionLabel && onAction && (
        <button className="btn ghost sm" onClick={onAction}>
          {actionLabel}
        </button>
      )}
    </div>
  )
}

/** Error state: never swallowed — retry plus an optional alternative path. */
export function ErrorBox({
  title = '加载失败',
  detail,
  retryLabel = '重试',
  onRetry,
  children,
}: {
  title?: string
  detail?: string
  retryLabel?: string
  onRetry?: () => void
  children?: ReactNode
}) {
  return (
    <div className="state-box error-box">
      <StateIco name="warn" />
      <p className="state-title">{title}</p>
      {detail && <p className="state-hint">{detail}</p>}
      {(onRetry || children) && (
        <div style={{ display: 'flex', gap: 8, justifyContent: 'center' }}>
          {onRetry && (
            <button className="btn sm" onClick={onRetry}>
              <StateIco name="refresh" />
              {retryLabel}
            </button>
          )}
          {children}
        </div>
      )}
    </div>
  )
}

/** Success state: tells the user what happened and where to go next. */
export function SuccessBox({
  title,
  detail,
  actionLabel,
  onAction,
}: {
  title: string
  detail?: string
  actionLabel?: string
  onAction?: () => void
}) {
  return (
    <div className="state-box success-box">
      <StateIco name="check" />
      <p className="state-title">{title}</p>
      {detail && <p className="state-hint">{detail}</p>}
      {actionLabel && onAction && (
        <button className="btn sm" onClick={onAction}>
          {actionLabel}
        </button>
      )}
    </div>
  )
}
