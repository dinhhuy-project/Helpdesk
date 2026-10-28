import type { ReactNode } from 'react'
import { toApiError } from '../api/api-error'
import { Button } from './button'

export function LoadingState({ label = 'Đang tải dữ liệu…' }: { label?: string }) {
  return <div className="async-state" role="status" aria-live="polite"><span className="spinner" aria-hidden="true" />{label}</div>
}

export function EmptyState({ title = 'Chưa có dữ liệu', description, action }: {
  title?: string; description?: string; action?: ReactNode
}) {
  return <div className="async-state"><h2>{title}</h2>{description && <p>{description}</p>}{action}</div>
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const problem = toApiError(error)
  return (
    <div className="async-state async-state--error" role="alert">
      <h2>Không thể tải dữ liệu</h2><p>{problem.message}</p>
      {problem.traceId && <p className="field-hint">Mã tra cứu: {problem.traceId}</p>}
      {problem.retryAfterSeconds !== undefined && <p>Vui lòng chờ {problem.retryAfterSeconds} giây trước khi thử lại.</p>}
      {onRetry && <Button variant="secondary" onClick={onRetry}>Thử lại</Button>}
    </div>
  )
}
