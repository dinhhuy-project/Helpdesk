import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { ticketKeys } from '../../tickets/hooks/use-tickets'
import { createSlaApi } from '../api/sla-api'
import type { SlaPage } from '../types/sla'

const sensitive = { staleTime: 0, gcTime: 0, refetchOnMount: 'always' as const }
export const slaKeys = {
  root: (scope: string | null) => ['sla-policies', scope] as const,
  lists: (scope: string | null) => ['sla-policies', scope, 'list'] as const,
  list: (scope: string | null, search: SlaPage) => ['sla-policies', scope, 'list', search] as const,
  detail: (scope: string | null, id: string) => ['sla-policies', scope, id] as const,
  ticket: (scope: string | null, id: string) => [...ticketKeys.resource(scope, id), 'sla'] as const,
}
export function useSlaPolicies(search: SlaPage, enabled: boolean) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: slaKeys.list(scope, search), enabled,
    queryFn: ({ signal }) => createSlaApi(client).list(search, signal) })
}
export function useSlaPolicy(id: string, enabled = true) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: slaKeys.detail(scope, id), enabled,
    queryFn: ({ signal }) => createSlaApi(client).detail(id, signal) })
}
export function useTicketSla(id: string) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: slaKeys.ticket(scope, id),
    queryFn: ({ signal }) => createSlaApi(client).ticket(id, signal),
    refetchInterval: query => query.state.data?.sla ? 60_000 : false, refetchIntervalInBackground: false })
}
