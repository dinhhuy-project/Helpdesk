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
import { Permission, readTicketRule } from '../../features/auth/lib/permissions'
import { TicketListPage } from '../../features/tickets/pages/ticket-list-page'
import { TicketDetailPage } from '../../features/tickets/pages/ticket-detail-page'
import { CreateTicketPage } from '../../features/tickets/pages/create-ticket-page'
import { AccountRouteBoundary, AccountListPage, AccountDetailPage, CreateAccountPage, RolesPage, RoleDetailPage, RoleAssignmentPage, AccountAuditPage } from '../../features/accounts/components/account-route-boundary'
import { CatalogRouteBoundary, CatalogListPage, CreateCatalogPage, CatalogDetailPage, TeamDetailPage } from '../../features/catalogs/components/catalog-route-boundary'
import { SlaRouteBoundary, SlaPolicyListPage, CreateSlaPolicyPage, SlaPolicyDetailPage } from '../../features/sla/components/sla-route-boundary'

export const appRoutes: RouteObject[] = [{
  path: '/',
  element: <RootLayout />,
  errorElement: <RouteErrorPage />,
  children: [
    { index: true, element: <HomePage /> },
    { path: 'login', element: <LoginPage /> },
    { path: '403', element: <ForbiddenPage /> },
    { element: <RequireSession />, children: [
      { path: 'admin/sla-policies', element: <SlaRouteBoundary />, children: [
        { index: true, element: <SlaPolicyListPage /> },
        { path: 'new', element: <CreateSlaPolicyPage /> },
        { path: ':id', element: <SlaPolicyDetailPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'account', element: <AccountPage /> },
      { path: 'account/password', element: <ChangePasswordPage /> },
      { path: 'admin/departments', element: <CatalogRouteBoundary kind="departments" />, children: [
        { index: true, element: <CatalogListPage kind="departments" /> },
        { path: 'new', element: <CreateCatalogPage kind="departments" /> },
        { path: ':id', element: <CatalogDetailPage kind="departments" /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'admin/teams', element: <CatalogRouteBoundary kind="teams" />, children: [
        { index: true, element: <CatalogListPage kind="teams" /> },
        { path: 'new', element: <CreateCatalogPage kind="teams" /> },
        { path: ':id', element: <TeamDetailPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'admin/categories', element: <CatalogRouteBoundary kind="categories" />, children: [
        { index: true, element: <CatalogListPage kind="categories" /> },
        { path: 'new', element: <CreateCatalogPage kind="categories" /> },
        { path: ':id', element: <CatalogDetailPage kind="categories" /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'admin/users', element: <AccountRouteBoundary />, children: [
        { index: true, element: <AccountListPage /> },
        { path: 'new', element: <CreateAccountPage /> },
        { path: ':id', element: <AccountDetailPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'admin/roles', element: <AccountRouteBoundary roles />, children: [
        { index: true, element: <RolesPage /> },
        { path: 'assignments', element: <AccountListPage assignment /> },
        { path: 'assignments/:id', element: <RoleAssignmentPage /> },
        { path: 'audit', element: <AccountAuditPage /> },
        { path: ':id', element: <RoleDetailPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'tickets/new', element: <RequirePermission rule={{ allOf: [Permission.TicketCreate] }} />, children: [
        { index: true, element: <CreateTicketPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
      { path: 'tickets', element: <RequirePermission rule={readTicketRule} />, children: [
        { index: true, element: <TicketListPage /> },
        { path: ':id', element: <TicketDetailPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
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
