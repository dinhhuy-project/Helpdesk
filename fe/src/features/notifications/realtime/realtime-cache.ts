import type { QueryClient } from '@tanstack/react-query'
import { ticketKeys } from '../../tickets/hooks/use-tickets'
import type { TicketAccess } from '../../tickets/types/ticket'
import { notificationKeys } from '../hooks/use-notifications'
import { reportKeys } from '../../dashboard/hooks/use-reports'

/** Revalidate access first; private observers unmount while that decision is pending. */
export async function refreshRealtimeTicket(queries: QueryClient, scope: string, id: string, current: () => boolean) {
  if (!current()) return
  const key = ticketKeys.resource(scope, id)
  void queries.invalidateQueries({ queryKey: reportKeys.root(scope) })
  await queries.cancelQueries({ queryKey: key })
  if (!current()) return
  await queries.invalidateQueries({ queryKey: key, refetchType: 'none' })
  const accessKey = ticketKeys.access(scope, id)
  const observed = queries.getQueryCache().find({ queryKey: accessKey, exact: true })?.getObserversCount()
  if (!observed) return
  await queries.invalidateQueries({ queryKey: accessKey })
  if (!current()) return
  const state = queries.getQueryState<TicketAccess>(accessKey)
  if (state?.status !== 'success' || !state.data?.canRead) {
    await queries.cancelQueries({ queryKey: key })
    if (current()) queries.removeQueries({ queryKey: key, predicate: query => query.queryKey[3] !== 'access' })
    return
  }
  await queries.invalidateQueries({ queryKey: key, predicate: query => query.queryKey[3] !== 'access' })
}
export async function refreshRealtimeInbox(queries: QueryClient, scope: string, current: () => boolean) {
  if (!current()) return
  await queries.cancelQueries({ queryKey: notificationKeys.root(scope) })
  if (!current()) return
  await Promise.all([
    queries.invalidateQueries({ queryKey: notificationKeys.root(scope) }),
    queries.invalidateQueries({ queryKey: ticketKeys.lists(scope) }),
    queries.invalidateQueries({ queryKey: reportKeys.root(scope) }),
  ])
}
