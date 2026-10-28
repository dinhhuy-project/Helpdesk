import { Outlet } from 'react-router-dom'
import { ForbiddenPage } from '../../../app/router/forbidden-page'
import { useAuth } from '../hooks/use-auth'
import { hasPermissions } from '../lib/permissions'
import type { PermissionRule } from '../lib/permissions'
import { CurrentProfileBoundary } from './current-profile-boundary'

function PermissionContent({ rule }: { rule: PermissionRule }) {
  const { user } = useAuth()
  return user && hasPermissions(user.permissions, rule) ? <Outlet /> : <ForbiddenPage />
}

export function RequirePermission({ rule }: { rule: PermissionRule }) {
  return <CurrentProfileBoundary><PermissionContent rule={rule} /></CurrentProfileBoundary>
}
