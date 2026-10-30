import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import type { AccountAccess, AccountAudit, AccountPermission, AccountSearch, AccountUser, CreateAccount, DepartmentOption, RoleAccess, RoleDetails, UpdateAccount } from '../types/account'

const users = '/api/v1/users'
const identity = '/api/v1/identity'
export function createAccountApi(client: HttpClient) {
  return {
    list: (params: AccountSearch, assignment: boolean, signal?: AbortSignal) =>
      client.get<PagedResult<AccountUser>>(assignment ? `${identity}/assignment-users` : users, { params, signal }),
    detail: (id: string, assignment: boolean, signal?: AbortSignal) =>
      client.get<AccountUser>(assignment ? `${identity}/assignment-users/${id}` : `${users}/${id}`, { signal }),
    access: (id: string, signal?: AbortSignal) => client.get<AccountAccess>(`${users}/${id}/administration-access`, { signal }),
    roleAccess: (signal?: AbortSignal) => client.get<RoleAccess>(`${identity}/administration-access`, { signal }),
    create: (body: CreateAccount, signal?: AbortSignal) => client.post<AccountUser>(users, body, { signal }),
    update: (id: string, body: UpdateAccount, signal?: AbortSignal) => client.patch<AccountUser>(`${users}/${id}`, body, { signal }),
    status: (id: string, isActive: boolean, version: number, signal?: AbortSignal) =>
      client.put<AccountUser>(`${users}/${id}/status`, { isActive, version }, { signal }),
    setRoles: (id: string, roleIds: string[], version: number, signal?: AbortSignal) =>
      client.put<AccountUser>(`${users}/${id}/roles`, { roleIds, version }, { signal }),
    roles: (signal?: AbortSignal) => client.get<RoleDetails[]>(`${identity}/roles`, { signal }),
    permissions: (signal?: AbortSignal) => client.get<AccountPermission[]>(`${identity}/permissions`, { signal }),
    grants: (id: string, permissionIds: string[], version: number, signal?: AbortSignal) =>
      client.put<RoleDetails>(`${identity}/roles/${id}/permissions`, { permissionIds, version }, { signal }),
    audit: (page: number, signal?: AbortSignal) => client.get<PagedResult<AccountAudit>>(`${identity}/audit-logs`, { params: { page, pageSize: 20 }, signal }),
    departments: (page: number, search: string, signal?: AbortSignal) => client.get<PagedResult<DepartmentOption>>('/api/v1/departments/lookup',
      { params: { page, pageSize: 20, search: search || undefined }, signal }),
  }
}
