import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { getTicketCategoryLookup } from '../api/ticket-category-api'
import type { CategoryLookupSearch } from '../types/ticket-category'

export const categoryLookupKeys = {
  scope: (scope: string | null) => ['ticket-category-lookup', scope] as const,
  list: (scope: string | null, search: CategoryLookupSearch) => ['ticket-category-lookup', scope, search] as const,
}
export function useTicketCategoryLookup(search: CategoryLookupSearch) {
  const { client, scope } = useAuth()
  return useQuery({ queryKey: categoryLookupKeys.list(scope, search), staleTime: 0, gcTime: 0,
    queryFn: ({ signal }) => getTicketCategoryLookup(client, search, signal) })
}
