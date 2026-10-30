import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import type { NotificationDetails, NotificationSearch, NotificationUnreadCount } from '../types/notification'

const root = '/api/v1/notifications'
function path(id: string) {
  if (!isGuid(id)) throw new ApiError('Mã thông báo không hợp lệ.', 'http', 404)
  return `${root}/${id}`
}
function verify(value: NotificationDetails, id?: string, read = false): NotificationDetails {
  if (!value || !isGuid(value.id) || !isGuid(value.ticketId) || !isGuid(value.activityId) || (id !== undefined && value.id !== id)
    || typeof value.title !== 'string' || typeof value.kind !== 'string' || typeof value.isInternal !== 'boolean'
    || !Number.isFinite(Date.parse(value.createdAt)) || (value.readAt !== null && !Number.isFinite(Date.parse(value.readAt))) || (read && value.readAt === null)) {
    throw new ApiError('Không xác nhận được dữ liệu thông báo.', 'unexpected')
  }
  // Only public DTO fields enter the cache; never retain arbitrary legacy content/transport fields.
  return { id: value.id, ticketId: value.ticketId, activityId: value.activityId, kind: value.kind, isInternal: value.isInternal,
    title: value.title, createdAt: value.createdAt, readAt: value.readAt }
}
export function createNotificationApi(client: HttpClient) {
  return {
    list: async (search: NotificationSearch, signal: AbortSignal) => {
      const value = await client.get<PagedResult<NotificationDetails>>(root, { params: search, signal })
      return { ...value, items: value.items.map(item => verify(item)) }
    },
    count: async (signal: AbortSignal) => {
      const value = await client.get<NotificationUnreadCount>(`${root}/unread-count`, { signal })
      if (!value || !Number.isSafeInteger(value.count) || value.count < 0) throw new ApiError('Không xác nhận được số thông báo chưa đọc.', 'unexpected')
      return { count: value.count }
    },
    detail: async (id: string, signal: AbortSignal) => verify(await client.get<NotificationDetails>(path(id), { signal }), id),
    markRead: async (id: string, signal: AbortSignal) => verify(await client.put<NotificationDetails>(`${path(id)}/read`, undefined, { signal }), id, true),
  }
}
