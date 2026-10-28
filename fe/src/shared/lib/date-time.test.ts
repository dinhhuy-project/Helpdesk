import { describe, expect, it } from 'vitest'
import { formatDateTime, formatUtcDay, toApiTimestamp } from './date-time'

describe('API timestamps and calendar dates', () => {
  it('converts an explicit offset to UTC for API filters', () => {
    expect(toApiTimestamp('2026-10-28T00:00:00+07:00')).toBe('2026-10-27T17:00:00.000Z')
  })
  it('rejects timezone-less datetime inputs rather than silently using the browser timezone', () => {
    expect(() => toApiTimestamp('2026-10-28T00:00:00')).toThrow('timezone')
  })
  it('formats a timestamp using the requested timezone', () => {
    expect(formatDateTime('2026-10-27T18:00:00Z', 'Asia/Bangkok')).toContain('28/10/26')
  })
  it('keeps daily report keys on the original UTC calendar day', () => {
    expect(formatUtcDay('2026-10-28')).toContain('28/10/26')
    expect(formatUtcDay('2026-02-30')).toBe('—')
  })
  it('handles missing/invalid display values without crashing the page', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
    expect(() => toApiTimestamp(new Date('invalid'))).toThrow()
  })
})
