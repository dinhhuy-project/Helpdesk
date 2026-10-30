import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { createNotificationApi } from '../api/notification-api'
import type { NotificationDetails, NotificationSearch } from '../types/notification'

export const notificationKeys = {
  root: (scope: string | null) => ['notifications', scope] as const,
  list: (scope: string | null, search: NotificationSearch) => ['notifications', scope, 'list', search] as const,
  detail: (scope: string | null, id: string) => ['notifications', scope, 'detail', id] as const,
  count: (scope: string | null) => ['notifications', scope, 'unread-count'] as const,
}
const sensitive = { staleTime: 0, gcTime: 0, refetchOnMount: 'always' as const,
  refetchInterval: 60_000, refetchIntervalInBackground: false }
export function useNotificationList(search: NotificationSearch, enabled: boolean) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: notificationKeys.list(scope, search), enabled,
    queryFn: ({ signal }) => createNotificationApi(client).list(search, signal) })
}
export function useNotificationDetail(id: string, enabled = true) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: notificationKeys.detail(scope, id), enabled,
    queryFn: ({ signal }) => createNotificationApi(client).detail(id, signal) })
}
export function useNotificationCount() {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: notificationKeys.count(scope),
    queryFn: ({ signal }) => createNotificationApi(client).count(signal) })
}
export function useMarkNotificationRead() {
  const { client, scope } = useAuth()
  const queries = useQueryClient()
  const command = useApiCommand()
  async function refresh() {
    await queries.cancelQueries({ queryKey: notificationKeys.root(scope) })
    if (command.isCurrent()) await queries.invalidateQueries({ queryKey: notificationKeys.root(scope) })
  }
  async function markRead(id: string) {
    return command.run(signal => createNotificationApi(client).markRead(id, signal), async receipt => {
      await queries.cancelQueries({ queryKey: notificationKeys.root(scope) })
      if (!command.isCurrent()) return
      queries.setQueryData<NotificationDetails>(notificationKeys.detail(scope, id), previous => previous ? receipt : undefined)
      // Refetch counts and all active filter/pages. Never decrement a cached count or infer the remaining rows.
      await queries.invalidateQueries({ queryKey: notificationKeys.root(scope) })
    })
  }
  return { ...command, markRead, refresh }
}
