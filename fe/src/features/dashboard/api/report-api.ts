import type { HttpClient } from '../../../shared/api/http-client'
import { reportFilter } from '../lib/report-search'
import type { DailyReport, DashboardReport, GroupedReport, ReportFilter, ReportSearch } from '../types/report'

export function createReportApi(client: HttpClient) {
  return {
    dashboard: (filter: ReportFilter, signal: AbortSignal) => client.get<DashboardReport>('/api/v1/dashboard', { params: reportFilter(filter), signal }),
    daily: (filter: ReportFilter, signal: AbortSignal) => client.get<DailyReport>('/api/v1/reports/tickets/daily', { params: reportFilter(filter), signal }),
    groups: (search: ReportSearch, signal: AbortSignal) => client.get<GroupedReport>(`/api/v1/reports/tickets/${search.view}`, {
      params: { ...reportFilter(search), page: search.page, pageSize: search.pageSize, sortBy: search.sortBy, descending: search.descending }, signal }),
  }
}
