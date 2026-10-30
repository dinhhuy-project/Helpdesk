import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { createTicketApi } from '../api/ticket-api'
import type { TicketSearch } from '../lib/ticket-search'
import type { CommentSearch } from '../types/ticket-discussion'

// Do not reuse cached resource access across a navigation or carry old rows between filters.
const sensitiveRead = { staleTime: 0, gcTime: 0, refetchOnMount: 'always' as const }
export const ticketKeys = {
  lists: (scope: string | null) => ['tickets', scope, 'list'] as const,
  list: (scope: string | null, search: TicketSearch) => ['tickets', scope, 'list', search] as const,
  access: (scope: string | null, id: string) => ['tickets', scope, id, 'access'] as const,
  detail: (scope: string | null, id: string) => ['tickets', scope, id, 'detail'] as const,
  workflow: (scope: string | null, id: string) => ['tickets', scope, id, 'workflow'] as const,
  resource: (scope: string | null, id: string) => ['tickets', scope, id] as const,
  discussionAccess: (scope: string | null, id: string) => ['tickets', scope, id, 'discussion-access'] as const,
  comments: (scope: string | null, id: string, search: CommentSearch) => ['tickets', scope, id, 'comments', search] as const,
  activities: (scope: string | null, id: string, page: number, canReadInternal = false) => ['tickets', scope, id, 'activities', { page, pageSize: 20, descending: true, visibility: canReadInternal ? 'authorized' : 'public' }] as const,
}
export function useTickets(search: TicketSearch) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.list(scope, search),
    queryFn: ({ signal }) => createTicketApi(client).list(search, signal) })
}
export function useTicketAccess(id: string) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.access(scope, id),
    queryFn: ({ signal }) => createTicketApi(client).access(id, signal) })
}
export function useTicketDetail(id: string) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.detail(scope, id),
    queryFn: ({ signal }) => createTicketApi(client).detail(id, signal) })
}
export function useTicketWorkflow(id: string, enabled: boolean) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.workflow(scope, id), enabled,
    queryFn: ({ signal }) => createTicketApi(client).workflow(id, signal) })
}
export function useTicketDiscussionAccess(id: string) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.discussionAccess(scope, id),
    queryFn: ({ signal }) => createTicketApi(client).discussionAccess(id, signal) })
}
export function useTicketComments(id: string, search: CommentSearch, enabled: boolean) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.comments(scope, id, search), enabled,
    queryFn: ({ signal }) => createTicketApi(client).comments(id, search, signal) })
}
export function useTicketActivities(id: string, page: number, enabled: boolean, canReadInternal = false) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitiveRead, queryKey: ticketKeys.activities(scope, id, page, canReadInternal), enabled,
    queryFn: ({ signal }) => createTicketApi(client).activities(id, page, signal, canReadInternal ? undefined : false) })
}
