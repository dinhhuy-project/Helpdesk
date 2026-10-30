import { useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useAccount, useAccountAccess, useAccountApi } from '../hooks/use-accounts'
import { useAccountCommand } from '../hooks/use-account-command'
import { useAuth } from '../../auth/hooks/use-auth'
import { Permission } from '../../auth/lib/permissions'
import { roleLabel } from '../../auth/lib/identity-labels'
import { AccountError, CommandFeedback } from '../components/account-state'
import { DepartmentPicker } from '../components/department-picker'
import { FormField, Input } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { ApiError, getFieldError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import { formatDateTime } from '../../../shared/lib/date-time'
import type { AccountAccess, AccountUser } from '../types/account'

export function AccountDetailPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <AccountDetails key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} />
    : <AccountError error={new ApiError('ID không hợp lệ.', 'http', 404)} />
}
function AccountDetails({ id }: { id: string }) {
  const account = useAccount(id)
  const access = useAccountAccess(id)
  const { user } = useAuth()
  const location = useLocation()
  const [revision, setRevision] = useState(0)
  const [saved, setSaved] = useState(false)
  const reload = async () => { await Promise.allSettled([account.refetch(), access.refetch()]); setRevision(n => n + 1) }
  if (account.error || access.error) return <AccountError error={account.error ?? access.error} onRetry={() => { void reload() }} />
  if (!account.data || !access.data) return <LoadingState />
  const value = account.data
  return <section className="account-page"><PageTitle title={value.displayName} />
    <div className="page-heading"><div><p className="eyebrow">Quản trị tài khoản</p><h1>{value.displayName}</h1><p>{value.email}</p></div><Link to="/admin/users">Về danh sách tài khoản</Link></div>
    {(saved || location.state?.accountCreated === true) && <p className="success-message" role="status">{saved ? 'Đã cập nhật tài khoản.' : 'Đã tạo tài khoản với vai trò Người yêu cầu.'}</p>}
    <dl className="account-summary panel"><div><dt>Trạng thái</dt><dd>{value.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</dd></div>
      <div><dt>Vai trò hiện tại</dt><dd>{value.roles.map(role => roleLabel(role.name)).join(', ') || 'Chưa có vai trò'}</dd></div>
      <div><dt>Ngày tạo</dt><dd>{formatDateTime(value.createdAt)}</dd></div><div><dt>Cập nhật lần cuối</dt><dd>{formatDateTime(value.updatedAt)}</dd></div>
    </dl>
    <AccountEditor key={`${value.version}:${revision}`} account={value} access={access.data} onReload={() => { void reload() }} onSaved={() => { setSaved(true); void reload() }} />
    {user?.permissions.includes(Permission.RoleManage) && user.id !== id && <p><Link to={`/admin/roles/assignments/${id}`}>Quản lý vai trò của tài khoản này</Link></p>}
    {user?.id === id && <p><Link to="/account/password">Tự đổi mật khẩu</Link> · Đổi mật khẩu thành công sẽ yêu cầu đăng nhập lại.</p>}
  </section>
}
function AccountEditor({ account, access, onReload, onSaved }: { account: AccountUser; access: AccountAccess; onReload: () => void; onSaved: () => void }) {
  const { api, scope } = useAccountApi()
  const { session, user } = useAuth()
  const command = useAccountCommand()
  const [name, setName] = useState(account.displayName)
  const [department, setDepartment] = useState(account.departmentId)
  const [confirm, setConfirm] = useState(false)
  const verified = (receipt: AccountUser) => {
    if (receipt?.id !== account.id || !receipt.version) throw new ApiError('Không xác nhận được thay đổi tài khoản.', 'unexpected')
    return receipt
  }
  return <div className="panel account-form">
    <h2>Thông tin tài khoản</h2>
    {!access.canUpdateProfile && <p role="status">Tài khoản được bảo vệ; bạn không có phạm vi chỉnh sửa thông tin hoặc trạng thái.</p>}
    {access.isLastActiveAdmin && <p role="status">Đây là quản trị hệ thống hoạt động cuối cùng. Không thể vô hiệu hóa.</p>}
    <form onSubmit={async event => {
      event.preventDefault()
      if (!access.canUpdateProfile || !name.trim()) return
      const receipt = await command.run(async signal => verified(await api.update(account.id,
        { displayName: name.trim(), departmentId: department, version: account.version }, signal)), async () => {
        if (user?.id === account.id) await session.refreshProfile({ background: true })
      })
      if (receipt) onSaved()
    }}>
      <FormField label="Tên hiển thị" required error={getFieldError(command.error, 'displayName')}>{props => <Input {...props} value={name} maxLength={200} disabled={!access.canUpdateProfile || command.pending} onChange={event => setName(event.target.value)} />}</FormField>
      <DepartmentPicker value={department} originalId={account.departmentId} originalName={account.departmentName} onChange={setDepartment} disabled={!access.canUpdateProfile || command.pending} />
      <CommandFeedback command={command} onReload={onReload} />
      <div className="account-actions"><Button type="submit" disabled={!access.canUpdateProfile || command.disabled || !name.trim()}>{command.pending ? 'Đang lưu…' : 'Lưu thông tin'}</Button>
        <Button variant="secondary" disabled={command.pending} onClick={onReload}>Tải lại thông tin</Button></div>
    </form>
    <div className="account-status-actions"><h2>Trạng thái tài khoản</h2><p>Thay đổi trạng thái sẽ thu hồi mọi phiên hiện có. Kích hoạt lại yêu cầu người dùng đăng nhập lại.</p>
      <Button variant={account.isActive ? 'danger' : 'secondary'} disabled={!access.canChangeStatus || command.disabled} onClick={() => setConfirm(true)}>
        {account.isActive ? 'Vô hiệu hóa tài khoản' : 'Kích hoạt tài khoản'}</Button>
    </div>
    <Dialog open={confirm} onOpenChange={open => { if (!command.pending) setConfirm(open) }} title={account.isActive ? 'Xác nhận vô hiệu hóa tài khoản' : 'Xác nhận kích hoạt tài khoản'}
      description={`${account.displayName} (${account.email}). Mọi phiên hiện có sẽ bị thu hồi.${user?.id === account.id ? ' Bạn sẽ thoát khỏi phiên hiện tại.' : ''}`}>
      <Button variant={account.isActive ? 'danger' : 'primary'} disabled={!access.canChangeStatus || command.disabled} onClick={async () => {
        const result = await command.run(async signal => verified(await api.status(account.id, !account.isActive, account.version, signal)), receipt => {
          if (!receipt.isActive && user?.id === account.id && scope) session.endRevokedSession(scope)
        })
        if (result) { setConfirm(false); onSaved() }
        else setConfirm(false)
      }}>{command.pending ? 'Đang cập nhật…' : 'Xác nhận thay đổi trạng thái'}</Button>
    </Dialog>
  </div>
}
