import { Link, useParams } from 'react-router-dom'
import { useState } from 'react'
import { useAccountApi, usePermissions, useRoleAccess, useRoles } from '../hooks/use-accounts'
import { useAccountCommand } from '../hooks/use-account-command'
import { useAuth } from '../../auth/hooks/use-auth'
import { permissionLabel, roleLabel } from '../../auth/lib/identity-labels'
import { AccountError, CommandFeedback } from '../components/account-state'
import { PageTitle } from '../../../shared/components/page-title'
import { DataTable } from '../../../shared/components/data-table'
import { LoadingState } from '../../../shared/components/async-state'
import { Dialog } from '../../../shared/components/dialog'
import { Button } from '../../../shared/components/button'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import type { AccountPermission, RoleDetails } from '../types/account'

export function RolesPage() {
  const access = useRoleAccess()
  const roles = useRoles()
  if (access.error || roles.error) return <AccountError error={access.error ?? roles.error} onRetry={() => { void access.refetch(); void roles.refetch() }} />
  if (!access.data || !roles.data) return <LoadingState />
  return <section className="account-page"><PageTitle title="Vai trò và quyền" />
    <p className="eyebrow">Quản trị quyền</p><h1>Vai trò và quyền</h1>
    <p>Cấp vai trò và quản lý grant tách biệt với chỉnh sửa thông tin tài khoản. Thay đổi quyền có hiệu lực ở các yêu cầu tiếp theo.</p>
    <nav className="account-actions" aria-label="Quản trị quyền"><Link className="button button--primary" to="/admin/roles/assignments">Gán vai trò cho tài khoản</Link>
      {access.data.canReadAudit && <Link to="/admin/roles/audit">Nhật ký quản trị</Link>}</nav>
    {!access.data.canManageGrants && <p role="status">Bạn có thể gán các vai trò trong phạm vi được ủy quyền. Chỉnh sửa grant và đọc audit cần quyền quản trị tương ứng từ server.</p>}
    <DataTable caption="Vai trò hiện có" rows={roles.data} rowKey={row => row.id} columns={[
      { key: 'name', header: 'Vai trò', render: row => <><Link to={`/admin/roles/${row.id}`}>{roleLabel(row.name)}</Link><p className="field-hint">{row.name}</p></> },
      { key: 'description', header: 'Mô tả', render: row => row.description || '—' },
      { key: 'permissions', header: 'Quyền hiện tại', render: row => <ul className="account-grant-summary">{row.permissions.map(code => <li key={code}>{permissionLabel(code)} <code>{code}</code></li>)}</ul> },
      { key: 'scope', header: 'Phạm vi cấp vai trò', render: row => access.data.assignableRoleIds.includes(row.id) ? 'Có thể cấp trong phạm vi tài khoản hợp lệ' : 'Ngoài phạm vi cấp của bạn' },
    ]} />
  </section>
}
export function RoleDetailPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <RoleDetail key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} />
    : <AccountError error={new ApiError('ID vai trò không hợp lệ.', 'http', 404)} />
}
function RoleDetail({ id }: { id: string }) {
  const access = useRoleAccess()
  const roles = useRoles()
  const permissions = usePermissions(access.data?.canManageGrants === true)
  const [revision, setRevision] = useState(0)
  const [saved, setSaved] = useState(false)
  const reload = async () => {
    await Promise.allSettled([access.refetch(), roles.refetch(), ...(access.data?.canManageGrants ? [permissions.refetch()] : [])])
    setRevision(n => n + 1)
  }
  if (access.error || roles.error || permissions.error) return <AccountError error={access.error ?? roles.error ?? permissions.error} onRetry={() => { void reload() }} />
  if (!access.data || !roles.data || (access.data.canManageGrants && !permissions.data)) return <LoadingState />
  const role = roles.data.find(item => item.id === id)
  if (!role) return <AccountError error={new ApiError('Không tìm thấy vai trò.', 'http', 404)} />
  return <section className="account-page"><PageTitle title={roleLabel(role.name) ?? role.name} /><p className="eyebrow">Vai trò hiện có</p><h1>{roleLabel(role.name)}</h1><p>{role.description}</p>
    <p><Link to="/admin/roles">Về vai trò và quyền</Link></p>
    {saved && <p role="status" className="success-message">Đã cập nhật grant của vai trò.</p>}
    {access.data.canManageGrants && permissions.data ? <GrantEditor key={`${role.version}:${revision}`} role={role} permissions={permissions.data} onReload={() => { void reload() }} onSaved={() => { setSaved(true); void reload() }} />
      : <div className="panel"><p>Bạn có quyền xem vai trò, không có quyền thay đổi grant.</p><ul>{role.permissions.map(code => <li key={code}>{permissionLabel(code)} <code>{code}</code></li>)}</ul></div>}
  </section>
}
function GrantEditor({ role, permissions, onSaved, onReload }: { role: RoleDetails; permissions: AccountPermission[]; onSaved: () => void; onReload: () => void }) {
  const { api } = useAccountApi()
  const { session } = useAuth()
  const command = useAccountCommand()
  const [selected, setSelected] = useState(permissions.filter(p => role.permissions.includes(p.code)).map(p => p.id))
  const [confirm, setConfirm] = useState(false)
  const unknownGrant = role.permissions.some(code => !permissions.some(p => p.code === code))
  return <form className="panel account-form" onSubmit={event => { event.preventDefault(); setConfirm(true) }}>
    <h2>Grant của vai trò</h2><p>Lưu sẽ thay thế toàn bộ grant bằng danh sách đã chọn và ảnh hưởng mọi tài khoản giữ vai trò này. Hệ thống bảo vệ các quyền quản trị bắt buộc.</p>
    {unknownGrant && <p role="alert">Có grant ngoài catalog hiện tại. Tải lại và kiểm tra trước khi thay đổi.</p>}
    <fieldset disabled={command.pending || unknownGrant}><legend>Quyền được cấp</legend>
      <div className="account-checks">{permissions.map(permission => <label key={permission.id} className="account-check">
        <input type="checkbox" checked={selected.includes(permission.id)} onChange={event => setSelected(previous => event.target.checked ? [...previous, permission.id] : previous.filter(id => id !== permission.id))} />
        <span>{permissionLabel(permission.code)}<code>{permission.code}</code></span>
      </label>)}</div>
    </fieldset>
    <CommandFeedback command={command} onReload={onReload} />
    <Button type="submit" disabled={command.disabled || unknownGrant}>Xem lại và lưu grant</Button>
    <Dialog open={confirm} onOpenChange={open => { if (!command.pending) setConfirm(open) }} title="Xác nhận thay thế grant"
      description={`${roleLabel(role.name)}: ${selected.length} quyền được chọn. Thay đổi áp dụng cho tất cả thành viên của vai trò.`}>
      <ul>{permissions.filter(p => selected.includes(p.id)).map(p => <li key={p.id}>{permissionLabel(p.code)} <code>{p.code}</code></li>)}</ul>
      {!selected.length && <p>Vai trò sẽ không còn permission nào.</p>}
      <Button disabled={command.disabled || unknownGrant} onClick={async () => {
        const receipt = await command.run(async signal => {
          const value = await api.grants(role.id, selected, role.version, signal)
          if (value?.id !== role.id || !value.version) throw new ApiError('Không xác nhận được grant đã lưu.', 'unexpected')
          return value
        }, () => session.refreshProfile())
        setConfirm(false)
        if (receipt) onSaved()
      }}>{command.pending ? 'Đang lưu…' : 'Xác nhận lưu grant'}</Button>
    </Dialog>
  </form>
}
