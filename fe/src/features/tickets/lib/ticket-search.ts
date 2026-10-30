import { toApiTimestamp } from '../../../shared/lib/date-time'
import type { TicketPriority, TicketStatus } from '../types/ticket-enums'

export interface TicketSearch {
  page: number
  pageSize: number
  search?: string
  ticketNumber?: string // Preserve the exact Int64 query value; never round it through Number.
  status?: TicketStatus
  priority?: TicketPriority
  requesterId?: string
  categoryId?: string
  teamId?: string
  agentId?: string
  unassigned?: boolean
  createdFrom?: string
  createdTo?: string
  sortBy: 'createdAt' | 'updatedAt' | 'ticketNumber' | 'priority'
  descending: boolean
}
export const defaultTicketSearch: TicketSearch = { page: 1, pageSize: 20, sortBy: 'createdAt', descending: true }
export function isTicketId(value: string | undefined): value is string {
  return !!value && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    && value !== '00000000-0000-0000-0000-000000000000'
}

type SearchResult = { value: TicketSearch; error?: never } | { error: string; value?: never }
export function parseTicketSearch(params: URLSearchParams): SearchResult {
  const value: TicketSearch = { ...defaultTicketSearch }
  try {
    const read = (key: string) => {
      if (params.getAll(key).length > 1) throw new Error(`Bộ lọc ${key} bị lặp.`)
      return params.get(key)?.trim() || undefined
    }
    const integer = (key: string, fallback: number, max: number) => {
      const raw = read(key)
      if (!raw) return fallback
      if (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > max) throw new Error(`Bộ lọc ${key} không hợp lệ.`)
      return Number(raw)
    }
    value.page = integer('page', 1, 1_000_000)
    value.pageSize = integer('pageSize', 20, 100)
    value.search = read('search')
    if (value.search && value.search.length > 200) throw new Error('Tiêu đề tìm kiếm tối đa 200 ký tự.')
    value.ticketNumber = read('ticketNumber')
    if (value.ticketNumber && (!/^\d{1,19}$/.test(value.ticketNumber) || BigInt(value.ticketNumber) < 1n
      || BigInt(value.ticketNumber) > 9223372036854775807n)) throw new Error('Số yêu cầu phải là số nguyên dương hợp lệ.')
    const status = read('status')
    if (status) {
      if (!/^[1-7]$/.test(status)) throw new Error('Trạng thái không hợp lệ.')
      value.status = Number(status) as TicketStatus
    }
    const priority = read('priority')
    if (priority) {
      if (!/^[1-4]$/.test(priority)) throw new Error('Độ ưu tiên không hợp lệ.')
      value.priority = Number(priority) as TicketPriority
    }
    for (const key of ['requesterId', 'categoryId', 'teamId', 'agentId'] as const) {
      const id = read(key)
      if (id && !isTicketId(id)) throw new Error(`Bộ lọc ${key} không hợp lệ.`)
      value[key] = id?.toLowerCase()
    }
    for (const key of ['unassigned', 'descending'] as const) {
      const raw = read(key)
      if (raw && raw !== 'true' && raw !== 'false') throw new Error(`Bộ lọc ${key} không hợp lệ.`)
      if (raw) value[key] = raw === 'true'
    }
    for (const key of ['createdFrom', 'createdTo'] as const) {
      const raw = read(key)
      if (!raw) continue
      const parts = /^(\d{4})-(\d{2})-(\d{2})T/.exec(raw)
      const calendar = parts ? new Date(`${parts[1]}-${parts[2]}-${parts[3]}T00:00:00Z`) : null
      if (!calendar || !Number.isFinite(calendar.getTime()) || calendar.toISOString().slice(0, 10) !== raw.slice(0, 10)) {
        throw new Error('Thời gian lọc phải là ISO 8601 hợp lệ có múi giờ.')
      }
      value[key] = toApiTimestamp(raw)
    }
    if (value.createdFrom && value.createdTo && value.createdFrom >= value.createdTo) {
      throw new Error('Thời điểm bắt đầu phải trước thời điểm kết thúc.')
    }
    const sort = read('sortBy')
    if (sort !== undefined) {
      if (sort !== 'createdAt' && sort !== 'updatedAt' && sort !== 'ticketNumber' && sort !== 'priority') {
        throw new Error('Cách sắp xếp không hợp lệ.')
      }
      value.sortBy = sort
    }
    return { value }
  } catch (error) {
    return { error: error instanceof Error ? error.message : 'Bộ lọc không hợp lệ.' }
  }
}

export function ticketSearchParams(value: TicketSearch): URLSearchParams {
  const params = new URLSearchParams()
  for (const [key, item] of Object.entries(value)) {
    if (item !== undefined && item !== '' && item !== defaultTicketSearch[key as keyof TicketSearch]) params.set(key, String(item))
  }
  return params
}

export function formatTicketNumber(value: number) {
  return Number.isSafeInteger(value) && value > 0 ? `HD-${String(value).padStart(6, '0')}` : 'Mã yêu cầu'
}

export function parseActivityPage(params: URLSearchParams): number | null {
  const value = params.get('activityPage') ?? '1'
  return params.getAll('activityPage').length <= 1 && /^\d+$/.test(value) && Number(value) >= 1 && Number(value) <= 1_000_000
    ? Number(value) : null
}
