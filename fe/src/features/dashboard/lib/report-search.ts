import { isGuid } from '../../../shared/lib/identifier'
import { toApiTimestamp } from '../../../shared/lib/date-time'
import type { ReportFilter, ReportSearch } from '../types/report'

export const defaultReportSearch: ReportSearch = { view: 'teams', page: 1, pageSize: 20, sortBy: 'total', descending: true }
export function reportFilter(value: ReportFilter): ReportFilter {
  // Whitelist the common cohort; pagination and presentation never alter aggregates.
  return { createdFrom: value.createdFrom, createdTo: value.createdTo, teamId: value.teamId,
    agentId: value.agentId, status: value.status, priority: value.priority }
}
export function reportSearchParams(value: ReportSearch) {
  const params = new URLSearchParams()
  for (const [key, item] of Object.entries({ ...reportFilter(value), view: value.view, page: value.page,
    pageSize: value.pageSize, sortBy: value.sortBy, descending: value.descending })) {
    if (item !== undefined && item !== defaultReportSearch[key as keyof ReportSearch]) params.set(key, String(item))
  }
  return params
}
export function parseReportSearch(params: URLSearchParams): { value: ReportSearch; error?: never } | { error: string; value?: never } {
  try {
    const read = (key: string) => {
      if (params.getAll(key).length > 1) throw new Error('Bộ lọc báo cáo bị lặp.')
      return params.get(key) || undefined
    }
    const value = { ...defaultReportSearch }
    for (const key of ['createdFrom', 'createdTo'] as const) {
      const raw = read(key)
      if (!raw) continue
      const day = raw.slice(0, 10)
      const calendar = new Date(`${day}T00:00:00Z`)
      if (!Number.isFinite(calendar.getTime()) || calendar.toISOString().slice(0, 10) !== day) throw new Error('Ngày lọc không hợp lệ.')
      value[key] = toApiTimestamp(raw)
    }
    if (!!value.createdFrom !== !!value.createdTo) throw new Error('Cần cả thời điểm bắt đầu và kết thúc.')
    if (value.createdFrom && value.createdTo) {
      const duration = Date.parse(value.createdTo) - Date.parse(value.createdFrom)
      if (duration <= 0 || duration > 366 * 86_400_000) throw new Error('Khoảng lọc phải tăng dần và không quá 366 ngày.')
    }
    for (const key of ['teamId', 'agentId'] as const) {
      const id = read(key)
      if (id && !isGuid(id)) throw new Error('Mã nhóm hoặc nhân viên không hợp lệ.')
      value[key] = id?.toLowerCase()
    }
    const status = read('status'), priority = read('priority')
    if (status && !/^[1-7]$/.test(status)) throw new Error('Trạng thái không hợp lệ.')
    if (priority && !/^[1-4]$/.test(priority)) throw new Error('Độ ưu tiên không hợp lệ.')
    if (status) value.status = Number(status) as ReportSearch['status']
    if (priority) value.priority = Number(priority) as ReportSearch['priority']
    const view = read('view'), sort = read('sortBy'), descending = read('descending')
    if (view && view !== 'teams' && view !== 'agents') throw new Error('Kiểu nhóm không hợp lệ.')
    if (sort && sort !== 'total' && sort !== 'open' && sort !== 'slaBreached') throw new Error('Cách sắp xếp không hợp lệ.')
    if (descending && descending !== 'true' && descending !== 'false') throw new Error('Chiều sắp xếp không hợp lệ.')
    if (view === 'teams' || view === 'agents') value.view = view
    if (sort === 'total' || sort === 'open' || sort === 'slaBreached') value.sortBy = sort
    if (descending) value.descending = descending === 'true'
    for (const [key, max] of [['page', 1_000_000], ['pageSize', 100]] as const) {
      const raw = read(key)
      if (!raw) continue
      if (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > max) throw new Error('Phân trang nhóm không hợp lệ.')
      value[key] = Number(raw)
    }
    return { value }
  } catch (error) {
    return { error: error instanceof Error ? error.message : 'Bộ lọc không hợp lệ.' }
  }
}

/** datetime-local shows the user's timezone; the URL/API retain an absolute instant. */
export function localDateTime(value: string | undefined) {
  if (!value) return ''
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 23)
}
