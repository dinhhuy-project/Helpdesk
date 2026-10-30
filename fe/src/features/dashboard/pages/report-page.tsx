import { useEffect } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { PageTitle } from '../../../shared/components/page-title'
import { Button } from '../../../shared/components/button'
import { ErrorState, LoadingState } from '../../../shared/components/async-state'
import { ApiError } from '../../../shared/api/api-error'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useAuth } from '../../auth/hooks/use-auth'
import { defaultReportSearch, parseReportSearch, reportSearchParams } from '../lib/report-search'
import { reportKeys, useReports } from '../hooks/use-reports'
import { ReportFilters } from '../components/report-filters'
import { DailyPanel, PeriodNote, ReportDistributions, ReportSlaPanel, ReportTotals } from '../components/report-panels'
import { ReportGroups } from '../components/report-groups'
import { ReportReload } from '../components/report-reload'
import type { ReportSearch } from '../types/report'

export function ReportPage({ reports = false }: { reports?: boolean }) {
  const [params, setParams] = useSearchParams()
  const { session, scope } = useAuth()
  const queries = useQueryClient()
  const parsed = parseReportSearch(params)
  const search = parsed.value ?? defaultReportSearch
  const query = useReports(search, reports, parsed.value !== undefined)
  const bootstrap = !search.createdFrom ? query.data?.dashboard.period : undefined
  useEffect(() => {
    if (bootstrap && !query.isFetching && !query.isError && parsed.value) {
      const anchored = parseReportSearch(reportSearchParams({ ...parsed.value, createdFrom: bootstrap.createdFrom, createdTo: bootstrap.createdTo }))
      if (anchored.value) setParams(reportSearchParams(anchored.value), { replace: true })
    }
  }, [bootstrap, parsed.value, query.isFetching, query.isError, setParams])
  const change = (value: ReportSearch) => setParams(reportSearchParams(value))
  const refresh = async () => {
    const expected = scope
    try { await session.refreshProfile() } catch { return }
    if (session.store.getState().scope === expected) await queries.invalidateQueries({ queryKey: reportKeys.root(scope) })
  }
  const title = reports ? 'Báo cáo Ticket' : 'Tổng quan'
  const problem = query.error instanceof ApiError ? query.error : undefined
  const data = !query.isFetching && !query.isError && search.createdFrom ? query.data : undefined
  return <section className="report-page"><PageTitle title={title} /><p className="eyebrow">Theo dõi</p><h1>{title}</h1>
    <p>Trạng thái hiện tại của các Ticket được tạo trong khoảng lọc, theo quyền truy cập hiện tại của bạn. Nhóm và nhân viên là phân công hiện tại.</p>
    <div className="report-page-actions"><Link to={`${reports ? '/dashboard' : '/reports/tickets'}${params.size ? `?${params}` : ''}`}>{reports ? 'Xem tổng quan cùng bộ lọc' : 'Xem báo cáo cùng bộ lọc'}</Link>
      <ReportReload pending={query.isFetching} retryAt={problem?.status === 429 ? query.errorUpdatedAt + (problem.retryAfterSeconds ?? 60) * 1000 : 0} reload={() => { void refresh() }} /></div>
    <ReportFilters key={params.toString()} search={search} change={change} />
    {parsed.error ? <div role="alert"><p>{parsed.error}</p><Button onClick={() => setParams({})}>Xóa bộ lọc</Button></div> :
      query.isError ? <>
        {problem?.status === 403 && <p role="alert">Bạn không còn quyền đọc báo cáo trong phạm vi hiện tại.</p>}
        {problem?.status === 409 && <p role="alert">Phạm vi truy cập đã thay đổi khi tổng hợp. Tải lại số liệu để kiểm tra quyền và lấy báo cáo mới.</p>}
        <ErrorState error={query.error} />
      </> : !data || !data.daily ? <LoadingState label="Đang kiểm tra và tổng hợp báo cáo…" /> : <>
        <p className="report-period">Tạo từ <strong>{formatDateTime(data.dashboard.period.createdFrom)}</strong> đến trước <strong>{formatDateTime(data.dashboard.period.createdTo)}</strong> ({Intl.DateTimeFormat().resolvedOptions().timeZone}).</p>
        <p className="field-hint">Mỗi API chốt số liệu riêng; thay đổi đồng thời có thể khiến các bảng tạm lệch nhau. Các mốc trong tương lai chỉ có dữ liệu đến thời điểm chốt.</p>
        <PeriodNote label="Tổng quan và SLA" period={data.dashboard.period} />
        {data.dashboard.tickets.total === 0 && <p role="status">Không có Ticket trong phạm vi và bộ lọc này.</p>}
        <ReportTotals tickets={data.dashboard.tickets} />
        <p className="field-hint">Đang mở gồm Mới, Đang xử lý, Chờ người yêu cầu và Mở lại. Chưa có nhóm/người xử lý tính trên toàn bộ tập lọc, bao gồm Ticket đã kết thúc.</p>
        <ReportDistributions dashboard={data.dashboard} /><ReportSlaPanel sla={data.dashboard.sla} />
        {data.grouped && <ReportGroups report={data.grouped} search={search} change={change} />}
        <DailyPanel report={data.daily} />
      </>}
    <p className="field-hint">Tự cập nhật mỗi phút khi tab hoạt động, khi trở lại tab hoặc kết nối lại; có thể tải lại thủ công.</p>
  </section>
}
