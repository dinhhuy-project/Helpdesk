import { Link } from 'react-router-dom'
import { ForbiddenPage } from '../../../app/router/forbidden-page'
import { toApiError } from '../../../shared/api/api-error'
import { EmptyState, ErrorState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { Button } from '../../../shared/components/button'
import type { useApiCommand } from '../../../shared/hooks/use-api-command'

export function CatalogError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const value = toApiError(error)
  if (value.status === 403) return <ForbiddenPage />
  if (value.status === 404) return <><PageTitle title="Không tìm thấy dữ liệu" /><EmptyState title="Không tìm thấy dữ liệu quản trị" description="Dữ liệu không tồn tại hoặc ngoài phạm vi được phép." /></>
  return <><PageTitle title="Không thể tải dữ liệu quản trị" /><ErrorState error={value} onRetry={onRetry} /></>
}
const messages: Record<string, string> = {
  NameInUse: 'Tên đã được sử dụng trong cùng phạm vi, kể cả dữ liệu đã vô hiệu hóa. Hãy chọn tên khác.',
  VersionConflict: 'Dữ liệu đã thay đổi. Bản nhập được giữ lại; tải lại và kiểm tra trước khi lưu tiếp.',
  InvalidDepartment: 'Phòng ban không tồn tại hoặc không còn hoạt động. Chọn lại phòng ban hợp lệ.',
  InactiveTeam: 'Nhóm hoặc phòng ban không còn hoạt động. Tải lại để kiểm tra trạng thái.',
  InvalidUser: 'Tài khoản không còn hoạt động hoặc không tồn tại. Chọn lại người dùng hợp lệ.',
  MembershipExists: 'Người dùng đã có trong lịch sử nhóm, kể cả khi đã ngừng tham gia. Dùng kích hoạt lại trong danh sách thành viên.',
  InvalidLeader: 'Trưởng nhóm cần tài khoản và việc tham gia hoạt động, có quyền quản lý nhóm, thuộc nhóm/phòng ban hoạt động.',
  InsufficientScope: 'Thao tác vượt phạm vi quản trị hiện tại. Quyền và membership được kiểm tra lại ở server.',
  InvalidParent: 'Danh mục cha không hợp lệ hoặc nhánh cha không còn khả dụng. Chọn lại hoặc chuyển về danh mục gốc.',
  CategoryCycle: 'Không thể đặt danh mục làm cha của chính mình hoặc chuyển vào nhánh con của nó.',
}
export function CatalogFeedback({ command, onReload, listPath }: { command: ReturnType<typeof useApiCommand>; onReload?: () => void; listPath?: string }) {
  if (!command.error) return null
  return <div className="account-feedback" role="alert"><p>{messages[command.error.code ?? ''] ?? command.error.message}</p>
    {command.error.traceId && <p className="field-hint">Mã tra cứu: {command.error.traceId}</p>}
    {command.remaining > 0 && <p>Vui lòng chờ {command.remaining} giây.</p>}
    {command.uncertain && <p>Chưa xác định được kết quả. Kiểm tra dữ liệu trước khi gửi yêu cầu mới; không gửi lại tự động.</p>}
    {onReload && !command.pending && <Button variant="secondary" onClick={onReload}>Tải lại dữ liệu</Button>}
    {listPath && command.uncertain && <Link to={listPath}>Kiểm tra danh sách</Link>}
  </div>
}
