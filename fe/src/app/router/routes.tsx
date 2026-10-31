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
import { NotificationRouteBoundary, NotificationListPage, NotificationDetailPage } from '../../features/notifications/components/notification-route-boundary'
import { ReportRouteBoundary, ReportPage } from '../../features/dashboard/components/report-route-boundary'
import { CommunityPublicBoundary } from '../../features/community/components/community-public-boundary'
import { CommunityPublicList, CommunityPublicDetail } from '../../features/community/pages/public-community-pages'
import { CommunityManageList, CommunityManageDetail } from '../../features/community/pages/manage-community-pages'

export const appRoutes: RouteObject[] = [{
  path: '/',
  element: <RootLayout />,
  errorElement: <RouteErrorPage />,
  children: [
    { index: true, element: <HomePage /> },
    { path: 'login', element: <LoginPage /> },
    { path: '403', element: <ForbiddenPage /> },
    { element: <CommunityPublicBoundary />, children: [
      { path: 'knowledge', element: <CommunityPublicList kind="knowledge" /> },
      { path: 'knowledge/:id', element: <CommunityPublicDetail kind="knowledge" /> },
      { path: 'incidents', element: <CommunityPublicList kind="incidents" /> },
      { path: 'incidents/:id', element: <CommunityPublicDetail kind="incidents" /> },
    ] },
    { element: <RequireSession />, children: [
      { element: <RequirePermission rule={{ anyOf: [Permission.KnowledgeManage, Permission.KnowledgePublish] }} />, children: [
        { path: 'admin/knowledge', element: <CommunityManageList kind="knowledge" /> },
        { path: 'admin/knowledge/new', element: <CommunityManageDetail kind="knowledge" /> },
        { path: 'admin/knowledge/:id', element: <CommunityManageDetail kind="knowledge" /> },
      ] },
      { element: <RequirePermission rule={{ anyOf: [Permission.IncidentManage, Permission.IncidentPublish] }} />, children: [
        { path: 'admin/incidents', element: <CommunityManageList kind="incidents" /> },
        { path: 'admin/incidents/new', element: <CommunityManageDetail kind="incidents" /> },
        { path: 'admin/incidents/:id', element: <CommunityManageDetail kind="incidents" /> },
      ] },
      { element: <ReportRouteBoundary />, children: [
        { path: 'dashboard', element: <ReportPage /> },
        { path: 'reports/tickets', element: <ReportPage reports /> },
      ] },
      { path: 'notifications', element: <NotificationRouteBoundary />, children: [
        { index: true, element: <NotificationListPage /> },
        { path: ':id', element: <NotificationDetailPage /> },
        { path: '*', element: <NotFoundPage /> },
      ] },
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
