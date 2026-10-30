import type { NotificationSearch } from '../types/notification'

export function parseNotificationSearch(params: URLSearchParams): { value?: NotificationSearch; error?: string } {
  const integer = (name: string, fallback: number, maximum: number) => {
    const raw = params.get(name)
    if (params.getAll(name).length > 1 || (raw !== null && (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > maximum))) throw new Error()
    return raw === null ? fallback : Number(raw)
  }
  try {
    const unread = params.get('unread')
    if (params.getAll('unread').length > 1 || (unread !== null && unread !== 'true' && unread !== 'false')) throw new Error()
    return { value: { page: integer('page', 1, 1_000_000), pageSize: integer('pageSize', 20, 100),
      ...(unread === null ? {} : { unread: unread === 'true' }) } }
  } catch { return { error: 'Bộ lọc hoặc phân trang thông báo không hợp lệ.' } }
}
export function notificationSearchUrl(search: NotificationSearch) {
  const params = new URLSearchParams()
  if (search.unread !== undefined) params.set('unread', String(search.unread))
  if (search.page !== 1) params.set('page', String(search.page))
  if (search.pageSize !== 20) params.set('pageSize', String(search.pageSize))
  return params
}
