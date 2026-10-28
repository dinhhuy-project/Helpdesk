import { Link } from 'react-router-dom'
import { ErrorState, LoadingState } from '../../../shared/components/async-state'
import { Button } from '../../../shared/components/button'
import { useAuth } from '../hooks/use-auth'
import { permissionLabel, roleLabel } from '../lib/identity-labels'
import { PageTitle } from '../../../shared/components/page-title'

export function AccountPage() {
  const { user, session, profilePending, profileError } = useAuth()
  if (profileError) return <ErrorState error={profileError} onRetry={() => { void session.refreshProfile().catch(() => {}) }} />
  if (profilePending) return <LoadingState label="Đang cập nhật tài khoản…" />
  if (!user) return <LoadingState />
  return (
    <section className="account-panel" aria-labelledby="account-title">
      <PageTitle title="Tài khoản của tôi" />
      <p className="eyebrow">Hồ sơ cá nhân</p><h1 id="account-title">Tài khoản của tôi</h1>
      <p className="auth-description">Thông tin và quyền hiện tại của bạn trong hệ thống.</p>
      <dl className="account-details">
        <div><dt>Họ tên</dt><dd>{user.displayName}</dd></div>
        <div><dt>Email</dt><dd>{user.email}</dd></div>
        <div><dt>Vai trò</dt><dd>{user.roles.length ? user.roles.map(roleLabel).join(', ') : 'Chưa được gán vai trò'}</dd></div>
      </dl>
      <div className="account-actions">
        <Link to="/account/password">Đổi mật khẩu</Link>
        <Button variant="secondary" pending={profilePending}
          onClick={() => { void session.refreshProfile().catch(() => {}) }}>Cập nhật thông tin</Button>
      </div>
      <details className="permission-details"><summary>Quyền hiện tại ({user.permissions.length})</summary>
        {user.permissions.length ? <ul>{user.permissions.map(permission => <li key={permission}>{permissionLabel(permission)}</li>)}</ul>
          : <p>Chưa có quyền nghiệp vụ. Liên hệ quản trị viên để được hỗ trợ.</p>}
      </details>
    </section>
  )
}
