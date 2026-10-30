import { EmptyState } from '../../../shared/components/async-state'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import type { PagedResult } from '../../../shared/api/pagination'
import type { TicketActivity } from '../types/ticket'
import { TicketStatus, getTicketStatusLabel } from '../types/ticket-enums'

const actionLabels: Record<string, string> = {
  TicketCreated: 'Đã tạo yêu cầu', TicketAssigned: 'Đã cập nhật phân công', TicketStatusChanged: 'Đã chuyển trạng thái',
  PublicCommentAdded: 'Đã thêm bình luận công khai', InternalNoteAdded: 'Đã thêm ghi chú nội bộ',
  PublicAttachmentAdded: 'Đã thêm tệp công khai', InternalAttachmentAdded: 'Đã thêm tệp nội bộ',
  SlaFirstResponseBreached: 'Quá hạn phản hồi đầu tiên', SlaResolutionBreached: 'Quá hạn giải quyết',
}
function statusLabel(value: string | null | undefined) {
  const number = Object.entries(TicketStatus).find(([name]) => name === value)?.[1]
  return number ? getTicketStatusLabel(number) : 'Không xác định'
}

export function TicketActivityList({ data, userId, onPageChange }: {
  data: PagedResult<TicketActivity>; userId?: string; onPageChange: (page: number) => void
}) {
  return <>
    {data.items.length ? <ol className="ticket-timeline">{data.items.map(activity => <li key={activity.id}>
      <div className="ticket-activity-header"><h3>{actionLabels[activity.action] ?? 'Yêu cầu đã được cập nhật'}</h3>
        {(activity.action === 'InternalNoteAdded' || activity.action === 'InternalAttachmentAdded') && <span className="comment-visibility">Nội bộ</span>}
        <time dateTime={activity.createdAt}>{formatDateTime(activity.createdAt)}</time></div>
      <p className="field-hint">{activity.actorId === userId ? 'Bạn' : activity.actorId ? 'Người dùng' : 'Hệ thống'}</p>
      {activity.action === 'TicketStatusChanged' && <>
        <p>{statusLabel(activity.oldValue?.status)} → {statusLabel(activity.newValue?.status)}</p>
        {activity.newValue?.resolutionSummary && <p className="ticket-plain-text">{activity.newValue.resolutionSummary}</p>}
      </>}
      {(activity.action === 'SlaFirstResponseBreached' || activity.action === 'SlaResolutionBreached') && activity.newValue?.dueAt &&
        <p>Hạn xử lý: {formatDateTime(activity.newValue.dueAt)}</p>}
    </li>)}</ol> : <EmptyState title="Chưa có hoạt động" description="Không có hoạt động hiển thị tại trang này." />}
    <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={onPageChange} />
  </>
}
