import { lazy, Suspense } from 'react'
import { RequirePermission } from '../../auth/components/require-permission'
import { Permission } from '../../auth/lib/permissions'
import { LoadingState } from '../../../shared/components/async-state'
import { useAuth } from '../../auth/hooks/use-auth'

export const SlaPolicyListPage = lazy(() => import('../pages/sla-policy-list-page').then(module => ({ default: module.SlaPolicyListPage })))
export const CreateSlaPolicyPage = lazy(() => import('../pages/create-sla-policy-page').then(module => ({ default: module.CreateSlaPolicyPage })))
export const SlaPolicyDetailPage = lazy(() => import('../pages/sla-policy-detail-page').then(module => ({ default: module.SlaPolicyDetailPage })))
export function SlaRouteBoundary() {
  const { scope } = useAuth()
  return <Suspense key={scope} fallback={<LoadingState />}><RequirePermission rule={{ allOf: [Permission.TicketManageSla] }} /></Suspense>
}
