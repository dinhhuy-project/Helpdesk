function parseTimestamp(value: string | Date): Date {
  if (typeof value === 'string' && !/^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/i.test(value)) {
    throw new Error('Timestamp phải là ISO 8601 có timezone.')
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) throw new Error('Timestamp không hợp lệ.')
  return date
}

export function toApiTimestamp(value: string | Date): string {
  return parseTimestamp(value).toISOString()
}

export function formatDateTime(value: string | null | undefined, timeZone?: string): string {
  if (!value) return '—'
  try {
    return new Intl.DateTimeFormat('vi-VN', {
      dateStyle: 'short', timeStyle: 'short', timeZone,
    }).format(parseTimestamp(value))
  } catch {
    return '—'
  }
}

/** Report daily keys represent UTC calendar days, not local timestamps. */
export function formatUtcDay(value: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return '—'
  const date = new Date(`${value}T00:00:00Z`)
  if (Number.isNaN(date.getTime()) || date.toISOString().slice(0, 10) !== value) return '—'
  return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeZone: 'UTC' }).format(date)
}
