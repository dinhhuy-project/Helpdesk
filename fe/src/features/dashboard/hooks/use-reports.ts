import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { createReportApi } from '../api/report-api'
import { reportFilter } from '../lib/report-search'
import type { DailyReport, DashboardReport, GroupedReport, ReportSearch } from '../types/report'
import { ApiError } from '../../../shared/api/api-error'

export const reportKeys = { root: (scope: string | null) => ['reports', scope] as const }
export interface ReportBundle { dashboard: DashboardReport; daily?: DailyReport; grouped?: GroupedReport }
export function useReports(search: ReportSearch, reports: boolean, enabled: boolean) {
  const { client, scope } = useAuth()
  const key = reports ? search : reportFilter(search)
  return useQuery<ReportBundle, ApiError>({ queryKey: [...reportKeys.root(scope), reports ? 'groups' : 'dashboard', key], enabled,
    staleTime: 0, gcTime: 0, refetchOnMount: 'always', refetchIntervalInBackground: false,
    refetchInterval: query => query.state.error instanceof ApiError && query.state.error.status === 429 ? false : 60_000,
    refetchOnWindowFocus: query => !(query.state.error instanceof ApiError && query.state.error.status === 429),
    refetchOnReconnect: query => !(query.state.error instanceof ApiError && query.state.error.status === 429),
    queryFn: async ({ signal }): Promise<ReportBundle> => {
      const api = createReportApi(client)
      // Bootstrap the server's default period, then pin it to the URL before requesting other aggregates.
      if (!search.createdFrom) return { dashboard: await api.dashboard(search, signal) }
      const [dashboard, daily, grouped] = await Promise.all([
        api.dashboard(search, signal), api.daily(search, signal), reports ? api.groups(search, signal) : undefined,
      ])
      // Publish a bundle only after every endpoint succeeds; a denied/changed scope cannot leave mixed old panels.
      return { dashboard, daily, grouped }
    } })
}
