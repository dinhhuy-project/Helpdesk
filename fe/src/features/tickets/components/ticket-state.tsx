import { Link } from 'react-router-dom'
import { ForbiddenPage } from '../../../app/router/forbidden-page'
import { ApiError } from '../../../shared/api/api-error'
import { ErrorState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'

export function TicketUnavailable() {
  return <section className="error-page"><PageTitle title="Không tìm thấy yêu cầu" />
    <p className="eyebrow">404</p><h1>Không tìm thấy yêu cầu</h1>
    <p>Yêu cầu không tồn tại hoặc bạn không có quyền truy cập.</p>
    <Link to="/tickets">Về danh sách yêu cầu</Link>
  </section>
}
export function TicketError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (error instanceof ApiError && error.status === 404) return <TicketUnavailable />
  if (error instanceof ApiError && error.status === 403) return <ForbiddenPage />
  return <><PageTitle title="Không thể tải yêu cầu" /><ErrorState error={error} onRetry={onRetry} /></>
}
