import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import type { CategoryLookupSearch, TicketCategoryLookup } from '../types/ticket-category'

export function getTicketCategoryLookup(client: HttpClient, search: CategoryLookupSearch, signal: AbortSignal) {
  return client.get<PagedResult<TicketCategoryLookup>>('/api/v1/ticket-categories/lookup', { params: search, signal })
}
