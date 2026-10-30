import { describe, expect, it } from 'vitest'
import { parseReportSearch, reportFilter, reportSearchParams } from './report-search'

describe('report cohort URL contract', () => {
  it('normalizes offsets to one absolute interval and round trips shared filters and group pagination', () => {
    const parsed = parseReportSearch(new URLSearchParams('createdFrom=2026-01-01T07:00:00%2B07:00&createdTo=2026-01-02T00:00:00Z&view=agents&page=3&pageSize=1&sortBy=open&descending=false&status=3&priority=4'))
    expect(parsed.value?.createdFrom).toBe('2026-01-01T00:00:00.000Z')
    expect(parsed.value).toBeDefined()
    if (!parsed.value) return
    expect(parseReportSearch(reportSearchParams(parsed.value))).toEqual(parsed)
    expect(reportFilter(parsed.value)).not.toHaveProperty('page')
    expect(reportFilter(parsed.value)).not.toHaveProperty('view')
  })
  it.each(['createdFrom=2026-01-01T00:00:00Z', 'createdFrom=2026-02-30T00:00:00Z&createdTo=2026-03-03T00:00:00Z',
    'createdFrom=2026-01-01T00:00:00&createdTo=2026-01-02T00:00:00Z',
    'createdFrom=2026-01-02T00:00:00Z&createdTo=2026-01-02T00:00:00Z',
    'createdFrom=2025-01-01T00:00:00Z&createdTo=2026-01-03T00:00:00Z',
    'page=0', 'pageSize=101', 'view=all', 'status=0', 'priority=5', 'sortBy=title', 'descending=no',
    'teamId=00000000-0000-0000-0000-000000000000', 'agentId=private', 'status=1&status=2'])('rejects invalid report query: %s', query => {
    expect(parseReportSearch(new URLSearchParams(query)).error).toBeTruthy()
  })
})
