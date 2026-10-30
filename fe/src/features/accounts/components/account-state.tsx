import { Link } from 'react-router-dom'
import { ForbiddenPage } from '../../../app/router/forbidden-page'
import { toApiError } from '../../../shared/api/api-error'
import { EmptyState, ErrorState } from '../../../shared/components/async-state'
import type { useAccountCommand } from '../hooks/use-account-command'

export function AccountError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const failure = toApiError(error)
  if (failure.status === 403) return <ForbiddenPage />
  if (failure.status === 404) return <EmptyState title="Không tìm thấy tài khoản hoặc vai trò" description="Dữ liệu không tồn tại hoặc nằm ngoài phạm vi quản trị của bạn." />
  return <ErrorState error={error} onRetry={onRetry} />
}
const messages: Record<string, string> = {
  VersionConflict: 'Dữ liệu đã được người khác thay đổi. Tải lại và kiểm tra trước khi lưu; bản nhập hiện tại chưa được áp dụng.',
  EmailInUse: 'Email này đã được sử dụng. Kiểm tra danh sách trước khi tạo lại.',
  LastActiveAdmin: 'Không thể vô hiệu hóa hoặc gỡ vai trò của quản trị hệ thống hoạt động cuối cùng.',
  ProtectedAdminGrant: 'Vai trò quản trị hệ thống phải giữ user.manage và role.manage.',
  InsufficientScope: 'Tài khoản hoặc vai trò nằm ngoài phạm vi cấp quyền hiện tại của bạn.',
  InvalidDepartment: 'Phòng ban đã bị vô hiệu hóa hoặc không còn tồn tại. Chọn lại phòng ban hợp lệ.',
  InvalidRole: 'Vai trò hoặc quyền đã thay đổi. Tải lại danh sách trước khi tiếp tục.',
}
export function CommandFeedback({ command, onReload, create = false }: {
  command: ReturnType<typeof useAccountCommand>; onReload?: () => void; create?: boolean
}) {
  if (!command.error) return null
  return <div className="account-feedback" role="alert">
    <p>{messages[command.error.code ?? ''] ?? command.error.message}</p>
    {command.error.traceId && <p className="field-hint">Mã tra cứu: {command.error.traceId}</p>}
    {command.remaining > 0 && <p>Vui lòng chờ {command.remaining} giây.</p>}
    {command.uncertain && <p>Chưa xác định được kết quả. {create ? 'Kiểm tra theo email trong danh sách trước khi tạo một yêu cầu mới.' : 'Tải lại dữ liệu để kiểm tra thay đổi trước khi thao tác tiếp.'} Không gửi lại tự động.</p>}
    {create && command.uncertain && <Link to="/admin/users">Kiểm tra danh sách tài khoản</Link>}
    {onReload && !command.pending && <button className="button button--secondary" type="button" onClick={onReload}>Tải lại dữ liệu</button>}
  </div>
}
