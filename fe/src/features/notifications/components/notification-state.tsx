import { Link } from 'react-router-dom'
import { ApiError } from '../../../shared/api/api-error'
import { ErrorState } from '../../../shared/components/async-state'
import { Button } from '../../../shared/components/button'
import type { useMarkNotificationRead } from '../hooks/use-notifications'

export function NotificationError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (error instanceof ApiError && (error.status === 403 || error.status === 404)) return <section className="async-state" role="alert">
    <h2>Thông báo không còn khả dụng</h2><p>Thông báo không tồn tại hoặc bạn không còn quyền xem.</p>
    <Link to="/notifications">Về danh sách thông báo</Link></section>
  return <ErrorState error={error} onRetry={onRetry} />
}
export function NotificationCommandFeedback({ command, success }: { command: ReturnType<typeof useMarkNotificationRead>; success: boolean }) {
  return <>
    {success && <p role="status" className="success-message">Đã đánh dấu thông báo là đã đọc.</p>}
    {command.error && <div role="alert" className="auth-message auth-message--error">
      <p>{command.error.status === 403 || command.error.status === 404 ? 'Thông báo không còn khả dụng; danh sách và số chưa đọc sẽ được kiểm tra lại.' : command.error.message}</p>
      {command.error.traceId && <p>Mã tra cứu: {command.error.traceId}</p>}
      {command.remaining > 0 && <p>Vui lòng chờ {command.remaining} giây trước khi thử lại.</p>}
    </div>}
    {command.uncertain && <p role="alert">Chưa xác định được kết quả đánh dấu. Tải lại để kiểm tra trạng thái đọc. Nếu vẫn chưa đọc, mở lại trang trước khi chủ động thử lại.</p>}
    {(command.error || success) && <Button variant="secondary" disabled={command.pending || command.remaining > 0} onClick={() => { void command.refresh() }}>Kiểm tra lại thông báo</Button>}
  </>
}
