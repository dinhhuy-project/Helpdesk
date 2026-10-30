import { lazy, Suspense } from 'react'
import { useAuth } from '../../auth/hooks/use-auth'
import { RequirePermission } from '../../auth/components/require-permission'
import { readTicketRule } from '../../auth/lib/permissions'
import { LoadingState } from '../../../shared/components/async-state'

export const ReportPage = lazy(() => import('../pages/report-page').then(module => ({ default: module.ReportPage })))
export function ReportRouteBoundary() {
  const { scope } = useAuth()
  return <Suspense key={scope} fallback={<LoadingState />}><RequirePermission rule={readTicketRule} /></Suspense>
}
