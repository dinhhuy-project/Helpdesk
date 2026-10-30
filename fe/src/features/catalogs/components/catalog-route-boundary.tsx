import { lazy, Suspense } from 'react'
import { RequirePermission } from '../../auth/components/require-permission'
import { Permission } from '../../auth/lib/permissions'
import type { PermissionCode } from '../../auth/lib/permissions'
import { LoadingState } from '../../../shared/components/async-state'
import type { CatalogKind } from '../types/catalog'

export const CatalogListPage = lazy(() => import('../pages/catalog-list-page').then(module => ({ default: module.CatalogListPage })))
export const CreateCatalogPage = lazy(() => import('../pages/create-catalog-page').then(module => ({ default: module.CreateCatalogPage })))
export const CatalogDetailPage = lazy(() => import('../pages/catalog-detail-page').then(module => ({ default: module.CatalogDetailPage })))
export const TeamDetailPage = lazy(() => import('../pages/team-detail-page').then(module => ({ default: module.TeamDetailPage })))
const permissions: Record<CatalogKind, PermissionCode> = { departments: Permission.DepartmentManage, teams: Permission.TeamManage, categories: Permission.CategoryManage }
export function CatalogRouteBoundary({ kind }: { kind: CatalogKind }) {
  return <Suspense fallback={<LoadingState />}><RequirePermission rule={{ allOf: [permissions[kind]] }} /></Suspense>
}
