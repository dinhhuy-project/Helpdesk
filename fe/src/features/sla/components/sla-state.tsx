import { Link } from 'react-router-dom'
import { ApiError } from '../../../shared/api/api-error'
import { ErrorState } from '../../../shared/components/async-state'
import { ForbiddenPage } from '../../../app/router/forbidden-page'
import type { useApiCommand } from '../../../shared/hooks/use-api-command'

export function SlaError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (error instanceof ApiError && error.status === 403) return <ForbiddenPage />
  if (error instanceof ApiError && error.status === 404) return <section className="error-page"><h1>Không tìm thấy policy SLA</h1><p>Policy không tồn tại hoặc bạn không có quyền truy cập.</p><Link to="/admin/sla-policies">Về danh sách policy</Link></section>
  return <ErrorState error={error} onRetry={onRetry} />
}
export function SlaCommandFeedback({ command }: { command: ReturnType<typeof useApiCommand> }) {
  return <>
    {command.error && <div role="alert" className="auth-message auth-message--error"><p>{command.error.code === 'ActivePolicyExists'
      ? 'Mức ưu tiên này đã có policy đang hoạt động. Vô hiệu hóa policy cũ hoặc chọn mức ưu tiên khác.'
      : command.error.code === 'VersionConflict' ? 'Policy đã thay đổi. Bản nhập được giữ; tải lại và xem xét trước khi gửi tiếp.' : command.error.message}</p>
      {command.error.traceId && <p>Mã tra cứu: {command.error.traceId}</p>}
      {command.remaining > 0 && <p>Vui lòng chờ {command.remaining} giây trước khi thử lại.</p>}</div>}
    {command.uncertain && <p role="alert">Chưa xác định được kết quả lưu. Kiểm tra danh sách/chi tiết trước khi chủ động mở lại workspace; hệ thống không tự gửi lại thao tác.</p>}
  </>
}
