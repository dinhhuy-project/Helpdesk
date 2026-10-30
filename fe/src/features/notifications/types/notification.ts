export interface NotificationDetails {
  id: string
  ticketId: string
  activityId: string
  kind: string
  isInternal: boolean
  title: string
  createdAt: string
  readAt: string | null
}
export interface NotificationSearch { page: number; pageSize: number; unread?: boolean }
export interface NotificationUnreadCount { count: number }
