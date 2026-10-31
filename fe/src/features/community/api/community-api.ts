import { apiClient, type HttpClient } from '../../../shared/api/http-client'

export type CommunityKind = 'knowledge' | 'incidents'
export interface CommunityPage<T> { items: T[]; page: number; pageSize: number; totalCount: number }
export interface PublicEntry {
  id: string; kind: 'Knowledge' | 'Incident'; title: string; summary: string; content: string | null; topic: string
  status: string | null; revision: number; publishedAt: string; sourceUrl: string
  affectedCount: number; helpfulCount: number; solvedCount: number
}
export interface ManagedEntry {
  id: string; kind: string; title: string; summary: string; content: string; topic: string; status: string | null
  isPublished: boolean; publicRevision: number; publishedAt: string | null; version: number; sourceTicketId: string | null; updatedAt: string
}
export interface EntryDraft { title: string; summary: string; content: string; topic: string; status: string | null; version: number }
export interface Participation { affected: boolean; helpful: boolean | null; solved: boolean }
export interface IncidentUpdate { id: string; message: string; status: string; revision: number; createdAt: string }
export interface CommunitySearch { page: number; search: string; topic?: string; active?: string }
export interface WikiReference { title: string; url: string; updatedAt: string | null }
export interface WikipediaResult { items: WikiReference[]; nextOffset: number | null }
export interface BookStackPage { id: number; title: string; updatedAt: string | null }
export interface BookStackPreview { pageId: number; title: string; content: string; revision: string; sourceUrl: string | null; attribution: string; license: string }
export interface KnowledgeImportResult { entry: ManagedEntry; alreadyImported: boolean }
export const communityLabels = { knowledge: 'Kho tri thức', incidents: 'Sự cố chung' } as const
export const incidentLabels: Record<string, string> = { Investigating: 'Đang điều tra', Identified: 'Đã xác định nguyên nhân', Monitoring: 'Đang theo dõi', Resolved: 'Đã khắc phục' }
export function communityQuery(search: CommunitySearch) {
  const params = new URLSearchParams({ page: String(search.page), pageSize: '12' })
  if (search.search) params.set('search', search.search)
  if (search.topic) params.set('topic', search.topic)
  if (search.active === 'true' || search.active === 'false') params.set('active', search.active)
  return params.toString()
}
// This transport has no session authorizer, even while a user is signed in.
export function createPublicCommunityApi(client: HttpClient = apiClient) {
  return {
    wikipedia: (search: string, offset: number, signal?: AbortSignal) => client.get<WikipediaResult>(`/api/v1/public/knowledge-sources/wikipedia?search=${encodeURIComponent(search)}&offset=${offset}`, { signal }),
    list: (kind: CommunityKind, search: CommunitySearch, signal?: AbortSignal) => client.get<CommunityPage<PublicEntry>>(`/api/v1/public/${kind}?${communityQuery(search)}`, { signal }),
    get: (kind: CommunityKind, id: string, signal?: AbortSignal) => client.get<PublicEntry>(`/api/v1/public/${kind}/${id}`, { signal }),
    updates: (id: string, page: number, signal?: AbortSignal) => client.get<CommunityPage<IncidentUpdate>>(`/api/v1/public/incidents/${id}/updates?page=${page}&pageSize=12`, { signal }),
  }
}
export const publicCommunityApi = createPublicCommunityApi()
export function createCommunityApi(client: HttpClient) {
  return {
    bookStackPages: (search: string, page: number, signal?: AbortSignal) => client.get<CommunityPage<BookStackPage>>(`/api/v1/community/knowledge-sources/bookstack/pages?search=${encodeURIComponent(search)}&page=${page}`, { signal }),
    bookStackPreview: (id: number, signal?: AbortSignal) => client.get<BookStackPreview>(`/api/v1/community/knowledge-sources/bookstack/pages/${id}`, { signal }),
    importBookStack: (pageId: number, signal?: AbortSignal) => client.post<KnowledgeImportResult>('/api/v1/community/knowledge-sources/bookstack/imports', { pageId }, { signal }),
    list: (kind: CommunityKind, search: CommunitySearch, signal?: AbortSignal) => client.get<CommunityPage<ManagedEntry>>(`/api/v1/community/${kind}?${communityQuery(search)}`, { signal }),
    get: (kind: CommunityKind, id: string, signal?: AbortSignal) => client.get<ManagedEntry>(`/api/v1/community/${kind}/${id}`, { signal }),
    save: (kind: CommunityKind, id: string | undefined, draft: EntryDraft, signal?: AbortSignal) => id
      ? client.put<ManagedEntry>(`/api/v1/community/${kind}/${id}`, draft, { signal })
      : client.post<ManagedEntry>(`/api/v1/community/${kind}`, draft, { signal }),
    fromTicket: (id: string, signal?: AbortSignal) => client.post<ManagedEntry>(`/api/v1/community/knowledge/from-ticket/${id}`, {}, { signal }),
    publish: (kind: CommunityKind, row: ManagedEntry, message: string, signal?: AbortSignal) => client.post<ManagedEntry>(`/api/v1/community/${kind}/${row.id}/publish`,
      { version: row.version, reviewedForPublic: true, message: message.trim() || null }, { signal }),
    withdraw: (kind: CommunityKind, row: ManagedEntry, signal?: AbortSignal) => client.post<ManagedEntry>(`/api/v1/community/${kind}/${row.id}/withdraw`, { version: row.version }, { signal }),
    participation: (kind: CommunityKind, id: string, signal?: AbortSignal) => client.get<Participation>(`/api/v1/community/${kind}/${id}/participation`, { signal }),
    participate: (kind: CommunityKind, id: string, value: boolean, solved: boolean, signal?: AbortSignal) => client.put<Participation>(`/api/v1/community/${kind}/${id}/participation`, { value, solved }, { signal }),
  }
}
