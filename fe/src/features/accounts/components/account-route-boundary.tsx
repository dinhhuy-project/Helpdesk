import { lazy, Suspense } from 'react'
import { RequirePermission } from '../../auth/components/require-permission'
import { Permission } from '../../auth/lib/permissions'
import { LoadingState } from '../../../shared/components/async-state'

export const AccountListPage = lazy(() => import('../pages/account-list-page').then(module => ({ default: module.AccountListPage })))
export const AccountDetailPage = lazy(() => import('../pages/account-detail-page').then(module => ({ default: module.AccountDetailPage })))
export const CreateAccountPage = lazy(() => import('../pages/create-account-page').then(module => ({ default: module.CreateAccountPage })))
export const RolesPage = lazy(() => import('../pages/roles-page').then(module => ({ default: module.RolesPage })))
export const RoleDetailPage = lazy(() => import('../pages/roles-page').then(module => ({ default: module.RoleDetailPage })))
export const RoleAssignmentPage = lazy(() => import('../pages/role-assignment-page').then(module => ({ default: module.RoleAssignmentPage })))
export const AccountAuditPage = lazy(() => import('../pages/account-audit-page').then(module => ({ default: module.AccountAuditPage })))

export function AccountRouteBoundary({ roles = false }: { roles?: boolean }) {
  return <Suspense fallback={<LoadingState />}><RequirePermission rule={{ allOf: [roles ? Permission.RoleManage : Permission.UserManage] }} /></Suspense>
}
