import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { createCatalogApi } from '../api/catalog-api'
import type { CatalogKind, CatalogSearch, LookupSearch, MemberSearch } from '../types/catalog'

const sensitive = { staleTime: 0, gcTime: 0, refetchOnMount: 'always' as const }
export const catalogKeys = {
  resource: (scope: string | null, kind: CatalogKind, id: string) => ['catalogs', scope, kind, id] as const,
  detail: (scope: string | null, kind: CatalogKind, id: string) => ['catalogs', scope, kind, id, 'detail'] as const,
}
export function useCatalogApi() {
  const { client, scope } = useAuth()
  return { api: createCatalogApi(client), scope }
}
export function useCatalogList<K extends CatalogKind>(kind: K, search: CatalogSearch) {
  const { api, scope } = useCatalogApi()
  return useQuery({ ...sensitive, queryKey: ['catalogs', scope, kind, 'list', search], queryFn: ({ signal }) => api.list(kind, search, signal) })
}
export function useCatalogDetail<K extends CatalogKind>(kind: K, id: string, enabled = true) {
  const { api, scope } = useCatalogApi()
  return useQuery({ ...sensitive, enabled, queryKey: catalogKeys.detail(scope, kind, id), queryFn: ({ signal }) => api.detail(kind, id, signal) })
}
export function useTeamAccess(id?: string, enabled = true) {
  const { api, scope } = useCatalogApi()
  return useQuery({ ...sensitive, enabled, queryKey: ['catalogs', scope, 'teams', id ?? 'global', 'access'], queryFn: ({ signal }) => api.teamAccess(id, signal) })
}
export function useTeamDepartments(search: LookupSearch) {
  const { api, scope } = useCatalogApi()
  return useQuery({ ...sensitive, queryKey: ['catalogs', scope, 'teams', 'departments', search], queryFn: ({ signal }) => api.teamDepartments(search, signal) })
}
export function useTeamMembers(id: string, search: MemberSearch, enabled: boolean) {
  const { api, scope } = useCatalogApi()
  return useQuery({ ...sensitive, enabled, queryKey: [...catalogKeys.resource(scope, 'teams', id), 'members', search], queryFn: ({ signal }) => api.members(id, search, signal) })
}
export function useTeamCandidates(id: string, search: LookupSearch) {
  const { api, scope } = useCatalogApi()
  return useQuery({ ...sensitive, queryKey: [...catalogKeys.resource(scope, 'teams', id), 'candidates', search], queryFn: ({ signal }) => api.candidates(id, search, signal) })
}
