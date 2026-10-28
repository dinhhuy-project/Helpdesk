import { Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../features/auth/hooks/use-auth'
import { ApplicationLayout } from './application-layout'
import { PublicLayout } from './public-layout'
import { RouteFocus } from '../router/route-focus'

export function RootLayout() {
  const auth = useAuth()
  const location = useLocation()
  const privateLayout = auth.status === 'authenticated' && location.pathname !== '/login'
  return <><RouteFocus />{privateLayout ? <ApplicationLayout><Outlet /></ApplicationLayout>
    : <PublicLayout><Outlet /></PublicLayout>}</>
}
