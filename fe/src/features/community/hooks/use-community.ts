import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { createCommunityApi, publicCommunityApi, type CommunityKind, type CommunitySearch } from '../api/community-api'

export const publicCommunityKey = ['public-community'] as const
export const communityReadOptions = { staleTime: 0, gcTime: 0, retry: false } as const
export function usePublicEntries(kind: CommunityKind, search: CommunitySearch) {
  return useQuery({ ...communityReadOptions, queryKey: [...publicCommunityKey, kind, search],
    queryFn: ({ signal }) => publicCommunityApi.list(kind, search, signal), refetchInterval: 30000 })
}
export function usePublicEntry(kind: CommunityKind, id: string) {
  return useQuery({ ...communityReadOptions, queryKey: [...publicCommunityKey, kind, id],
    queryFn: ({ signal }) => publicCommunityApi.get(kind, id, signal), refetchInterval: 30000 })
}
export function useCommunityApi() {
  const { client, scope, user } = useAuth()
  return { api: createCommunityApi(client), scope, user }
}
