import { lazy, Suspense } from 'react'
import { Outlet } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { LoadingState } from '../../../shared/components/async-state'

export const NotificationListPage = lazy(() => import('../pages/notification-list-page').then(module => ({ default: module.NotificationListPage })))
export const NotificationDetailPage = lazy(() => import('../pages/notification-detail-page').then(module => ({ default: module.NotificationDetailPage })))
export function NotificationRouteBoundary() {
  const { scope } = useAuth()
  return <Suspense key={scope} fallback={<LoadingState />}><Outlet /></Suspense>
}
