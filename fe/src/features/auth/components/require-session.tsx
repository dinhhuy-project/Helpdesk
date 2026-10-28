import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { LoadingState } from '../../../shared/components/async-state'
import { useAuth } from '../hooks/use-auth'
import { PageTitle } from '../../../shared/components/page-title'

export function RequireSession() {
  const auth = useAuth()
  const location = useLocation()
  if (auth.status === 'authenticating') return <><PageTitle title="Đang xác thực" /><LoadingState label="Đang xác thực tài khoản…" /></>
  if (auth.status !== 'authenticated') {
    const returnTo = auth.reason === 'passwordChanged' || auth.reason === 'passwordUncertain' ? '/account'
      : `${location.pathname}${location.search}${location.hash}`
    return <Navigate to={`/login?returnTo=${encodeURIComponent(returnTo)}`} replace />
  }
  return <Outlet />
}
