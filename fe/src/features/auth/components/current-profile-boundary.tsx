import type { ReactNode } from 'react'
import { ErrorState, LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { useAuth } from '../hooks/use-auth'

export function CurrentProfileBoundary({ children }: { children: ReactNode }) {
  const auth = useAuth()
  if (auth.status !== 'authenticated' || auth.profilePending || !auth.user) {
    return <><PageTitle title="Đang xác thực" /><LoadingState label="Đang xác thực tài khoản và quyền truy cập…" /></>
  }
  if (auth.profileError) return <><PageTitle title="Không thể xác nhận tài khoản" />
    <ErrorState error={auth.profileError} onRetry={() => { void auth.session.refreshProfile().catch(() => {}) }} /></>
  return children
}
