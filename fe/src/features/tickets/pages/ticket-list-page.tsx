import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { hasPermissions, Permission } from '../../auth/lib/permissions'
import { Button } from '../../../shared/components/button'
import { PageTitle } from '../../../shared/components/page-title'
import { DataTable } from '../../../shared/components/data-table'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { TicketFilters } from '../components/ticket-filters'
import { TicketError } from '../components/ticket-state'
import { isTicketAccessError } from '../lib/ticket-errors'
import { useTickets } from '../hooks/use-tickets'
import { defaultTicketSearch, formatTicketNumber, parseTicketSearch, ticketSearchParams } from '../lib/ticket-search'
import type { TicketSearch } from '../lib/ticket-search'
import { getTicketPriorityLabel, getTicketStatusLabel } from '../types/ticket-enums'

export function TicketListPage() {
  const { user } = useAuth()
  const [params, setParams] = useSearchParams()
  const parsed = parseTicketSearch(params)
  const change = (value: TicketSearch) => setParams(ticketSearchParams(value))
  return <section className="ticket-page"><PageTitle title="Danh sách yêu cầu" />
    <header className="ticket-page-heading"><p className="eyebrow">Hỗ trợ</p><h1>Danh sách yêu cầu</h1>
      <p>Các yêu cầu bạn được phép truy cập. Kết quả và tổng số được cập nhật theo bộ lọc.</p>
      {hasPermissions(user?.permissions ?? [], { allOf: [Permission.TicketCreate] }) && <Link className="button button--primary ticket-new-link" to="/tickets/new">Tạo yêu cầu</Link>}
    </header>
    {parsed.error !== undefined ? <div className="ticket-card" role="alert"><h2>Bộ lọc chưa hợp lệ</h2><p>{parsed.error}</p>
      <Button variant="secondary" onClick={() => change(defaultTicketSearch)}>Xóa bộ lọc</Button></div>
      : <><TicketFilters key={params.toString()} value={parsed.value} onChange={change} />
        <TicketResults value={parsed.value} onChange={change} /></>}
  </section>
}

function TicketResults({ value, onChange }: { value: TicketSearch; onChange: (value: TicketSearch) => void }) {
  const query = useTickets(value)
  const { user } = useAuth()
  const location = useLocation()
  if (isTicketAccessError(query.error)) return <TicketError error={query.error} onRetry={() => { void query.refetch() }} />
  return <section className="ticket-results" aria-label="Kết quả yêu cầu" aria-busy={query.isFetching}>
    <DataTable caption="Danh sách yêu cầu hỗ trợ" rows={query.data?.items ?? []} rowKey={ticket => ticket.id}
      loading={query.isPending || query.isFetching} error={query.error} onRetry={() => { void query.refetch() }}
      emptyMessage="Không có yêu cầu phù hợp trong phạm vi truy cập và trang hiện tại."
      columns={[
        { key: 'title', header: 'Yêu cầu', render: ticket => <Link className="ticket-title-link" to={`/tickets/${ticket.id}`}
          state={{ ticketListUrl: `${location.pathname}${location.search}` }}><span className="ticket-number">{formatTicketNumber(ticket.ticketNumber)}</span><span>{ticket.title}</span></Link> },
        { key: 'status', header: 'Trạng thái', render: ticket => <span className="ticket-badge">{getTicketStatusLabel(ticket.status)}</span> },
        { key: 'priority', header: 'Ưu tiên', render: ticket => <span className={`ticket-priority ticket-priority-${ticket.priority}`}>{getTicketPriorityLabel(ticket.priority)}</span> },
        { key: 'agent', header: 'Người xử lý', render: ticket => ticket.assignedAgentId === user?.id ? 'Bạn' : ticket.assignedAgentId ? 'Đã phân công' : 'Chưa phân công' },
        { key: 'createdAt', header: 'Ngày tạo', render: ticket => <time dateTime={ticket.createdAt}>{formatDateTime(ticket.createdAt)}</time> },
      ]} />
    {query.data && !query.isError && !query.isFetching && <Pagination page={query.data.page} pageSize={query.data.pageSize}
      totalCount={query.data.totalCount} onPageChange={page => onChange({ ...value, page })} />}
  </section>
}
