import { Button } from '../../../shared/components/button'
import { DataTable } from '../../../shared/components/data-table'
import { FormField, Select } from '../../../shared/components/form-field'
import { Pagination } from '../../../shared/components/pagination'
import type { GroupedReport, ReportSearch } from '../types/report'
import { PeriodNote } from './report-panels'
import { reportNumber, reportPercent } from '../lib/report-format'

export function ReportGroups({ report, search, change }: { report: GroupedReport; search: ReportSearch; change: (value: ReportSearch) => void }) {
  const title = search.view === 'teams' ? 'Theo nhóm hỗ trợ' : 'Theo nhân viên xử lý'
  return <section className="report-card"><h2>{title}</h2><PeriodNote label={title} period={report.period} />
    <div className="report-group-controls">
      <FormField label="Nhóm báo cáo">{field => <Select {...field} value={search.view} onChange={event => change({ ...search, view: event.target.value as ReportSearch['view'], page: 1 })}>
        <option value="teams">Nhóm hỗ trợ</option><option value="agents">Nhân viên xử lý</option></Select>}</FormField>
      <FormField label="Sắp xếp nhóm">{field => <Select {...field} value={search.sortBy} onChange={event => change({ ...search, sortBy: event.target.value as ReportSearch['sortBy'], page: 1 })}>
        <option value="total">Tổng Ticket</option><option value="open">Đang mở</option><option value="slaBreached">Ticket vi phạm SLA</option></Select>}</FormField>
      <FormField label="Chiều sắp xếp">{field => <Select {...field} value={String(search.descending)} onChange={event => change({ ...search, descending: event.target.value === 'true', page: 1 })}>
        <option value="true">Giảm dần</option><option value="false">Tăng dần</option></Select>}</FormField>
      <FormField label="Nhóm mỗi trang">{field => <Select {...field} value={search.pageSize} onChange={event => change({ ...search, pageSize: Number(event.target.value), page: 1 })}>
        {![10, 20, 50, 100].includes(search.pageSize) && <option value={search.pageSize}>{search.pageSize}</option>}
        {[10, 20, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}</Select>}</FormField>
    </div>
    <p className="field-hint">{reportNumber(report.groups.totalCount)} nhóm có dữ liệu trong phạm vi này; phân trang và sắp xếp do server thực hiện. Chọn tên để lọc chung; nhóm chưa phân công được giữ trong bảng, không có bộ lọc UUID tương ứng.</p>
    <DataTable caption={title} rows={report.groups.items} rowKey={row => row.id ?? 'unassigned'} emptyMessage="Không có nhóm trong trang này. Thử trang trước hoặc đổi bộ lọc."
      columns={[
        { key: 'name', header: search.view === 'teams' ? 'Nhóm hỗ trợ' : 'Nhân viên xử lý', render: row => <>
          {row.id === null ? 'Chưa phân công' : <Button variant="secondary" onClick={() => change({ ...search, page: 1, [search.view === 'teams' ? 'teamId' : 'agentId']: row.id! })}>{row.name}</Button>}
          {row.isActive === false && <span className="ticket-badge">Ngừng hoạt động</span>}</> },
        ...([['total', 'Tổng Ticket'], ['open', 'Đang mở'], ['resolved', 'Đã giải quyết'], ['closed', 'Đã đóng'], ['cancelled', 'Đã hủy'],
          ['unassignedTeam', 'Chưa có nhóm'], ['unassignedAgent', 'Chưa có người xử lý']] as const).map(([key, header]) => ({ key, header,
          render: (row: GroupedReport['groups']['items'][number]) => reportNumber(row.tickets[key]) })),
        { key: 'breached', header: 'Ticket vi phạm SLA', render: row => reportNumber(row.sla.breachedTickets) },
        { key: 'response', header: 'Đạt SLA phản hồi', render: row => reportPercent(row.sla.firstResponse.metPercent) },
        { key: 'resolution', header: 'Đạt SLA giải quyết', render: row => reportPercent(row.sla.resolution.metPercent) },
      ]} />
    <Pagination page={report.groups.page} pageSize={report.groups.pageSize} totalCount={report.groups.totalCount} onPageChange={page => change({ ...search, page })} />
  </section>
}
