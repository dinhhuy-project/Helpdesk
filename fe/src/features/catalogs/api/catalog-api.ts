import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import type { CatalogCreates, CatalogKind, CatalogRecords, CatalogSearch, CatalogUpdates, LookupOption, LookupSearch, MemberSearch, MembershipResult, TeamAccess, TeamMember, TeamMemberCandidate } from '../types/catalog'

const paths: Record<CatalogKind, string> = { departments: '/api/v1/departments', teams: '/api/v1/support-teams', categories: '/api/v1/ticket-categories' }
export function createCatalogApi(client: HttpClient) {
  return {
    list: <K extends CatalogKind>(kind: K, params: CatalogSearch, signal?: AbortSignal) => client.get<PagedResult<CatalogRecords[K]>>(paths[kind], { params, signal }),
    detail: <K extends CatalogKind>(kind: K, id: string, signal?: AbortSignal) => client.get<CatalogRecords[K]>(`${paths[kind]}/${id}`, { signal }),
    create: <K extends CatalogKind>(kind: K, value: CatalogCreates[K], signal?: AbortSignal) => client.post<CatalogRecords[K]>(paths[kind], value, { signal }),
    update: <K extends CatalogKind>(kind: K, id: string, value: CatalogUpdates[K], signal?: AbortSignal) => client.patch<CatalogRecords[K]>(`${paths[kind]}/${id}`, value, { signal }),
    status: <K extends CatalogKind>(kind: K, id: string, isActive: boolean, version: number, signal?: AbortSignal) =>
      client.put<CatalogRecords[K]>(`${paths[kind]}/${id}/status`, { isActive, version }, { signal }),
    teamAccess: (id?: string, signal?: AbortSignal) => client.get<TeamAccess>(`${paths.teams}${id ? `/${id}` : ''}/administration-access`, { signal }),
    teamDepartments: (params: LookupSearch, signal?: AbortSignal) => client.get<PagedResult<LookupOption>>(`${paths.teams}/department-lookup`, { params, signal }),
    members: (id: string, params: MemberSearch, signal?: AbortSignal) => client.get<PagedResult<TeamMember>>(`${paths.teams}/${id}/members`, { params, signal }),
    candidates: (id: string, params: LookupSearch, signal?: AbortSignal) => client.get<PagedResult<TeamMemberCandidate>>(`${paths.teams}/${id}/member-candidates`, { params, signal }),
    addMember: (id: string, userId: string, teamVersion: number, signal?: AbortSignal) => client.post<MembershipResult>(`${paths.teams}/${id}/members`, { userId, teamVersion }, { signal }),
    memberStatus: (id: string, userId: string, isActive: boolean, version: number, teamVersion: number, signal?: AbortSignal) =>
      client.put<MembershipResult>(`${paths.teams}/${id}/members/${userId}/status`, { isActive, version, teamVersion }, { signal }),
    leadership: (id: string, userId: string, isLeader: boolean, version: number, teamVersion: number, signal?: AbortSignal) =>
      client.put<MembershipResult>(`${paths.teams}/${id}/members/${userId}/leadership`, { isLeader, version, teamVersion }, { signal }),
    removeMember: (id: string, userId: string, version: number, teamVersion: number, signal?: AbortSignal) =>
      client.delete(`${paths.teams}/${id}/members/${userId}`, { params: { version, teamVersion }, signal }),
  }
}
