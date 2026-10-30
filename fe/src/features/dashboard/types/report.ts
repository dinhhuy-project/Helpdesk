import type { PagedResult } from '../../../shared/api/pagination'
import type { TicketPriority, TicketStatus } from '../../tickets/types/ticket-enums'

export interface ReportFilter {
  createdFrom?: string
  createdTo?: string
  teamId?: string
  agentId?: string
  status?: TicketStatus
  priority?: TicketPriority
}
export interface ReportSearch extends ReportFilter {
  view: 'teams' | 'agents'
  page: number
  pageSize: number
  sortBy: 'total' | 'open' | 'slaBreached'
  descending: boolean
}
export interface ReportPeriod { createdFrom: string; createdTo: string; asOf: string }
export interface TicketTotals {
  total: number; open: number; resolved: number; closed: number; cancelled: number
  unassignedTeam: number; unassignedAgent: number
}
export interface SlaMetric {
  met: number; breached: number; pending: number; stopped: number; unknown: number
  measured: number; metPercent: number | null; durationSamples: number; averageMinutes: number | null
}
export interface ReportSla {
  withSla: number; withoutSla: number; excludedCancelled: number; eligible: number; breachedTickets: number
  firstResponse: SlaMetric; resolution: SlaMetric
}
export interface DashboardReport {
  period: ReportPeriod
  tickets: TicketTotals
  byStatus: { key: string; count: number }[]
  byPriority: { key: string; count: number }[]
  sla: ReportSla
}
export interface TicketReportGroup { id: string | null; name: string; isActive: boolean | null; tickets: TicketTotals; sla: ReportSla }
export interface GroupedReport { period: ReportPeriod; groups: PagedResult<TicketReportGroup> }
export interface DailyReport { period: ReportPeriod; days: { date: string; createdTickets: number }[] }
