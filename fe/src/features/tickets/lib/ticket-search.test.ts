import { describe, expect, it } from 'vitest'
import { defaultTicketSearch, formatTicketNumber, isTicketId, parseActivityPage, parseTicketSearch, ticketSearchParams } from './ticket-search'

describe('Ticket URL contract', () => {
  it('uses backend defaults and preserves a full filtered direct link', () => {
    expect(parseTicketSearch(new URLSearchParams())).toEqual({ value: defaultTicketSearch })
    const result = parseTicketSearch(new URLSearchParams({ page: '3', pageSize: '50', search: ' VPN  ', status: '2', priority: '4',
      requesterId: '00000000-0000-0000-0000-000000000001', teamId: '00000000-0000-0000-0000-000000000002',
      categoryId: '00000000-0000-0000-0000-000000000003', agentId: '00000000-0000-0000-0000-000000000004',
      unassigned: 'false', descending: 'false', sortBy: 'priority', createdFrom: '2026-10-29T07:00:00+07:00',
      createdTo: '2026-10-30T00:00:00Z', ticketNumber: '9223372036854775807' }))
    expect(result.value).toMatchObject({ page: 3, pageSize: 50, search: 'VPN', status: 2, priority: 4, unassigned: false,
      descending: false, createdFrom: '2026-10-29T00:00:00.000Z', ticketNumber: '9223372036854775807' })
    expect(result.value && parseTicketSearch(ticketSearchParams(result.value))).toEqual(result)
  })
  it.each(['page=0', 'page=-1', 'page=1.5', 'page=1000001', 'pageSize=101', 'status=0', 'status=New', 'priority=5',
    'unassigned=1', 'descending=no', 'teamId=bad', 'agentId=00000000-0000-0000-0000-000000000000',
    'sortBy=description', 'ticketNumber=0', 'ticketNumber=9223372036854775808', 'ticketNumber=1e3',
    'status=1&status=2', 'page=1&page=2', 'createdFrom=2026-02-30T00:00:00Z',
    'createdFrom=2026-10-29T07:00:00', 'createdFrom=2026-10-30T00:00:00Z&createdTo=2026-10-29T00:00:00Z'])('rejects malformed URL without broadening a filter: %s', raw => {
    expect(parseTicketSearch(new URLSearchParams(raw)).error).toEqual(expect.any(String))
  })
  it('rejects overlong search and omits defaults from the URL', () => {
    expect(parseTicketSearch(new URLSearchParams({ search: 'x'.repeat(201) })).error).toBeTruthy()
    expect(ticketSearchParams(defaultTicketSearch).toString()).toBe('')
    expect(ticketSearchParams({ ...defaultTicketSearch, unassigned: false }).toString()).toBe('unassigned=false')
  })
  it('validates direct resource/activity links and avoids rounding an unsafe JSON number', () => {
    expect(isTicketId('00000000-0000-0000-0000-000000000001')).toBe(true)
    expect(isTicketId('../users')).toBe(false)
    expect(isTicketId('00000000-0000-0000-0000-000000000000')).toBe(false)
    expect(parseActivityPage(new URLSearchParams('activityPage=2'))).toBe(2)
    expect(parseActivityPage(new URLSearchParams('activityPage=0'))).toBeNull()
    expect(parseActivityPage(new URLSearchParams('activityPage=1&activityPage=2'))).toBeNull()
    expect(formatTicketNumber(42)).toBe('HD-000042')
    expect(formatTicketNumber(Number.MAX_SAFE_INTEGER + 1)).toBe('Mã yêu cầu')
  })
})
