import { useEffect } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../../shared/api/api-error'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { Button } from '../../../shared/components/button'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useAuth } from '../../auth/hooks/use-auth'
import { Permission } from '../../auth/lib/permissions'
import { getTicketPriorityLabel } from '../../tickets/types/ticket-enums'
import { useTicketSla } from '../hooks/use-sla'

export function TicketSlaPanel({ id, onDenied }: { id: string; onDenied: () => void }) {
  const query = useTicketSla(id)
  const { user } = useAuth()
  const denied = query.error instanceof ApiError && (query.error.status === 403 || query.error.status === 404)
  useEffect(() => { if (denied) onDenied() }, [denied, onDenied])
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone
  return <section className="ticket-card sla-panel" aria-labelledby="ticket-sla-heading"><div className="comment-section-heading">
    <h2 id="ticket-sla-heading">SLA của yêu cầu</h2><Button variant="secondary" disabled={query.isFetching} onClick={() => { void query.refetch() }}>Tải lại SLA</Button></div>
    {query.isPending || query.isFetching ? <LoadingState label="Đang tải SLA…" /> : query.isError ? <ErrorState error={query.error} /> : query.data.sla === null ?
      <EmptyState title="Yêu cầu chưa có SLA" description="Không có snapshot SLA cho Ticket này. Tạo hoặc bật policy sau đó không gán SLA hồi tố cho Ticket cũ." /> : <>
        <p>Policy khi áp dụng: <strong>{query.data.sla.policyName ?? 'Không lưu tên policy lịch sử'}</strong></p>
        <dl className="ticket-information"><div><dt>Ưu tiên khi áp dụng</dt><dd>{query.data.sla.priority === null ? 'Không lưu snapshot ưu tiên' : getTicketPriorityLabel(query.data.sla.priority)}</dd></div>
          <div><dt>Mục tiêu phản hồi đầu tiên</dt><dd>{query.data.sla.firstResponseMinutes === null ? 'Không lưu mục tiêu lịch sử' : `${query.data.sla.firstResponseMinutes.toLocaleString('vi-VN')} phút`}</dd></div>
          <div><dt>Mục tiêu giải quyết</dt><dd>{query.data.sla.resolutionMinutes === null ? 'Không lưu mục tiêu lịch sử' : `${query.data.sla.resolutionMinutes.toLocaleString('vi-VN')} phút`}</dd></div>
          <div><dt>Áp dụng lúc</dt><dd>{formatDateTime(query.data.sla.appliedAt)}</dd></div>
        </dl>
        <div className="sla-metrics">
          <SlaMetric title="Phản hồi công khai đầu tiên" dueAt={query.data.sla.firstResponseDueAt} completedAt={query.data.sla.firstRespondedAt}
            breached={query.data.sla.firstResponseBreached} breachedAt={query.data.sla.firstResponseBreachedAt} completeLabel="Đã ghi nhận phản hồi hợp lệ" pendingLabel="Chưa ghi nhận phản hồi hợp lệ" />
          <SlaMetric title="Giải quyết" dueAt={query.data.sla.resolutionDueAt} completedAt={query.data.sla.resolvedAt}
            breached={query.data.sla.resolutionBreached} breachedAt={query.data.sla.resolutionBreachedAt} completeLabel="Đã giải quyết" pendingLabel="Chưa ghi nhận giải quyết" />
        </div>
        {(query.data.status === 4 || query.data.status === 5 || query.data.status === 6) && <p className="field-hint">Ticket đã giải quyết hoặc kết thúc. Các mốc phản hồi và vi phạm đã ghi vẫn được giữ; resolution summary không thay thế phản hồi công khai.</p>}
        <details><summary>Tham chiếu policy của snapshot</summary><p>{query.data.sla.policyId}</p>
          {user?.permissions.includes(Permission.TicketManageSla) && <Link to={`/admin/sla-policies/${query.data.sla.policyId}`}>Xem policy hiện tại</Link>}
          <p className="field-hint">Policy hiện tại có thể đã thay đổi; các mục tiêu và deadline ở panel này lấy từ snapshot Ticket.</p></details>
      </>}
    {query.data && !query.isError && !query.isFetching && <p className="field-hint">Đánh giá tại server: {formatDateTime(query.data.asOf)}. Múi giờ hiển thị: {zone}.</p>}
    <p className="field-hint">SLA tính 24/7, kể cả ngày nghỉ. Chờ người yêu cầu không tạm dừng SLA. Chỉ phản hồi công khai hợp lệ của người xử lý được tính; internal note và file không tính phản hồi đầu tiên.</p>
  </section>
}
function SlaMetric({ title, dueAt, completedAt, breached, breachedAt, completeLabel, pendingLabel }: {
  title: string; dueAt: string; completedAt: string | null; breached: boolean; breachedAt: string | null; completeLabel: string; pendingLabel: string
}) {
  return <section className={`sla-metric${breached ? ' sla-metric--breached' : ''}`} aria-label={title}><h3>{title}</h3>
    <p className="sla-outcome">{breached ? 'Đã vi phạm SLA' : 'Chưa ghi nhận vi phạm SLA'}</p>
    <dl><dt>Hạn chót</dt><dd>{formatDateTime(dueAt)}</dd><dt>{completedAt ? completeLabel : pendingLabel}</dt><dd>{formatDateTime(completedAt)}</dd>
      <dt>Ghi nhận vi phạm lần đầu</dt><dd>{breachedAt ? formatDateTime(breachedAt) : breached ? 'Server xác định đã vi phạm; chưa có mốc ghi nhận từ command/worker.' : '—'}</dd></dl>
  </section>
}
