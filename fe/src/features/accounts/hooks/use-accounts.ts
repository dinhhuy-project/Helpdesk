import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { createAccountApi } from '../api/account-api'
import type { AccountSearch } from '../types/account'

const sensitive = { staleTime: 0, gcTime: 0, refetchOnMount: 'always' as const }
export function useAccountApi() {
  const { client, scope } = useAuth()
  return { api: createAccountApi(client), scope }
}
export function useAccounts(search: AccountSearch, assignment = false) {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, queryKey: ['accounts', scope, assignment ? 'assignment-users' : 'users', search],
    queryFn: ({ signal }) => api.list(search, assignment, signal) })
}
export function useAccount(id: string, assignment = false) {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, queryKey: ['accounts', scope, assignment ? 'assignment-user' : 'user', id],
    queryFn: ({ signal }) => api.detail(id, assignment, signal) })
}
export function useAccountAccess(id: string) {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, queryKey: ['accounts', scope, 'access', id], queryFn: ({ signal }) => api.access(id, signal) })
}
export function useRoleAccess() {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, queryKey: ['accounts', scope, 'role-access'], queryFn: ({ signal }) => api.roleAccess(signal) })
}
export function useRoles() {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, queryKey: ['accounts', scope, 'roles'], queryFn: ({ signal }) => api.roles(signal) })
}
export function usePermissions(enabled: boolean) {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, enabled, queryKey: ['accounts', scope, 'permissions'], queryFn: ({ signal }) => api.permissions(signal) })
}
export function useAudit(page: number, enabled: boolean) {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, enabled, queryKey: ['accounts', scope, 'audit', page], queryFn: ({ signal }) => api.audit(page, signal) })
}
export function useDepartments(page: number, search: string) {
  const { api, scope } = useAccountApi()
  return useQuery({ ...sensitive, queryKey: ['accounts', scope, 'departments', page, search], queryFn: ({ signal }) => api.departments(page, search, signal) })
}
