import { DataTable } from '../../../shared/components/data-table'
import { formatDateTime, formatUtcDay } from '../../../shared/lib/date-time'
import { TicketPriority, TicketStatus, ticketPriorityLabels, ticketStatusLabels } from '../../tickets/types/ticket-enums'
import type { DailyReport, DashboardReport, ReportPeriod, ReportSla, TicketTotals } from '../types/report'
import { reportNumber, reportPercent } from '../lib/report-format'

export function PeriodNote({ period, label }: { period: ReportPeriod; label: string }) {
  return <p className="field-hint">{label} · Chốt số liệu lúc {formatDateTime(period.asOf)}</p>
}
export function ReportTotals({ tickets }: { tickets: TicketTotals }) {
  const values: [keyof TicketTotals, string][] = [['total', 'Tổng Ticket'], ['open', 'Đang mở'], ['resolved', 'Đã giải quyết'],
    ['closed', 'Đã đóng'], ['cancelled', 'Đã hủy'], ['unassignedTeam', 'Chưa có nhóm'], ['unassignedAgent', 'Chưa có người xử lý']]
  return <dl className="report-totals" aria-label="Chỉ số Ticket">{values.map(([key, label]) => <div key={key}><dt>{label}</dt><dd>{reportNumber(tickets[key])}</dd></div>)}</dl>
}
function CountBars({ title, rows }: { title: string; rows: { key: string; label: string; count: number }[] }) {
  const maximum = Math.max(1, ...rows.map(row => row.count))
  return <section className="report-card"><h2>{title}</h2><ul className="report-bars" aria-label={title}>{rows.map(row => <li key={row.key}>
    <div><span>{row.label}</span><strong>{reportNumber(row.count)}</strong></div>
    <span className="report-bar" aria-hidden="true"><span style={{ width: `${row.count / maximum * 100}%` }} /></span>
  </li>)}</ul></section>
}
export function ReportDistributions({ dashboard }: { dashboard: DashboardReport }) {
  return <div className="report-columns">
    <CountBars title="Theo trạng thái" rows={dashboard.byStatus.map(row => ({ ...row,
      label: ticketStatusLabels[TicketStatus[row.key as keyof typeof TicketStatus]] ?? row.key }))} />
    <CountBars title="Theo độ ưu tiên" rows={dashboard.byPriority.map(row => ({ ...row,
      label: ticketPriorityLabels[TicketPriority[row.key as keyof typeof TicketPriority]] ?? row.key }))} />
  </div>
}
export function ReportSlaPanel({ sla }: { sla: ReportSla }) {
  return <section className="report-card"><h2>SLA của tập Ticket</h2>
    <dl className="report-sla-summary">
      {([['Có SLA', sla.withSla], ['Không có SLA', sla.withoutSla], ['Đã hủy, loại khỏi SLA', sla.excludedCancelled],
        ['Đủ điều kiện SLA', sla.eligible], ['Ticket vi phạm SLA', sla.breachedTickets]] as const).map(([label, count]) => <div key={label}><dt>{label}</dt><dd>{reportNumber(count)}</dd></div>)}
    </dl>
    <DataTable caption="Chỉ số SLA" rows={[{ name: 'Phản hồi công khai đầu tiên', ...sla.firstResponse }, { name: 'Giải quyết', ...sla.resolution }]} rowKey={row => row.name}
      columns={[
        { key: 'name', header: 'Chỉ số', render: row => row.name },
        { key: 'metPercent', header: 'Tỷ lệ đạt', render: row => reportPercent(row.metPercent) },
        ...([['met', 'Đạt'], ['breached', 'Vi phạm'], ['pending', 'Đang chờ'], ['stopped', 'Đã dừng'], ['unknown', 'Chưa xác định'], ['measured', 'Mẫu đánh giá'], ['durationSamples', 'Mẫu thời lượng']] as const)
          .map(([key, header]) => ({ key, header, render: (row: { name: string } & typeof sla.firstResponse) => reportNumber(row[key]) })),
        { key: 'average', header: 'Trung bình (phút)', render: row => row.averageMinutes === null ? 'Chưa có mẫu đo' : reportNumber(row.averageMinutes) },
      ]} />
    <p className="field-hint">Tỷ lệ dùng mẫu đánh giá đạt + vi phạm từ API. Thời lượng trung bình dùng mẫu thời lượng riêng. Đang chờ, đã dừng, chưa xác định và không có SLA không được xem là đạt.</p>
  </section>
}
export function DailyPanel({ report }: { report: DailyReport }) {
  const max = Math.max(1, ...report.days.map(day => day.createdTickets))
  return <section className="report-card"><h2>Ticket tạo theo ngày UTC</h2>
    <PeriodNote label="Theo ngày" period={report.period} />
    <p className="field-hint">Ngày UTC giữ nguyên trên mọi múi giờ. Ngày đầu/cuối có thể chỉ bao gồm một phần ngày; đây không phải số Ticket giải quyết trong ngày.</p>
    {report.days.length > 0 && <div className="report-daily-chart" aria-hidden="true">{report.days.map(day => <span key={day.date}
      title={`${formatUtcDay(day.date.slice(0, 10))}: ${reportNumber(day.createdTickets)}`}><span style={{ height: `${day.createdTickets / max * 100}%` }} /></span>)}</div>}
    <details><summary>Xem bảng số liệu từng ngày</summary><DataTable caption="Ticket tạo từng ngày UTC" rows={report.days} rowKey={row => row.date}
      columns={[{ key: 'date', header: 'Ngày UTC', render: row => formatUtcDay(row.date.slice(0, 10)) },
        { key: 'count', header: 'Ticket được tạo', render: row => reportNumber(row.createdTickets) }]} /></details>
  </section>
}
