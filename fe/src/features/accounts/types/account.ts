export interface AccountRole { id: string; name: string }
export interface AccountUser {
  id: string; email: string; displayName: string; isActive: boolean; departmentId: string | null; departmentName: string | null
  createdAt: string; updatedAt: string | null; version: number; roles: AccountRole[]
}
export interface RoleDetails extends AccountRole { description: string | null; version: number; permissions: string[] }
export interface AccountPermission { id: string; code: string }
export interface AccountAccess { canUpdateProfile: boolean; canChangeStatus: boolean; isLastActiveAdmin: boolean }
export interface RoleAccess { canManageGrants: boolean; canReadAudit: boolean; assignableRoleIds: string[] }
export interface AccountAudit {
  id: string; actorId: string | null; targetUserId: string | null; targetRoleId: string | null
  action: string; data: string | null; createdAt: string
}
export interface DepartmentOption { id: string; name: string }
export interface CreateAccount { email: string; displayName: string; password: string }
export interface UpdateAccount { displayName: string; departmentId: string | null; version: number }
export interface AccountSearch {
  page: number; pageSize: number; search?: string; isActive?: boolean
  sortBy: 'createdAt' | 'email' | 'displayName'; descending: boolean
}
