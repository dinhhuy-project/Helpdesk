import { createBrowserRouter } from 'react-router-dom'
import type { RouteObject } from 'react-router-dom'
import { HomePage } from '../../features/home/pages/home-page'
import { RootLayout } from '../layouts/root-layout'
import { NotFoundPage } from './not-found-page'
import { RouteErrorPage } from './route-error-page'
import { LoginPage } from '../../features/auth/pages/login-page'
import { AccountPage } from '../../features/auth/pages/account-page'
import { ChangePasswordPage } from '../../features/auth/pages/change-password-page'
import { RequireSession } from '../../features/auth/components/require-session'
import { RequirePermission } from '../../features/auth/components/require-permission'
import { navigation } from '../navigation/navigation'
import { ForbiddenPage } from './forbidden-page'

export const appRoutes: RouteObject[] = [{
  path: '/',
  element: <RootLayout />,
  errorElement: <RouteErrorPage />,
  children: [
    { index: true, element: <HomePage /> },
    { path: 'login', element: <LoginPage /> },
    { path: '403', element: <ForbiddenPage /> },
    { element: <RequireSession />, children: [
      { path: 'account', element: <AccountPage /> },
      { path: 'account/password', element: <ChangePasswordPage /> },
      // Reserved module paths enforce UX permissions now; authorized users still see 404
      // until the actual feature page is implemented. Disabled navigation never links here.
      ...navigation.filter(item => !item.available).map(item => ({
        path: `${item.path.slice(1)}/*`,
        element: <RequirePermission rule={item.rule ?? {}} />,
        children: [{ path: '*', element: <NotFoundPage /> }],
      })),
    ] },
    { path: '*', element: <NotFoundPage /> },
  ],
}]

export function createAppRouter() {
  return createBrowserRouter(appRoutes)
}
