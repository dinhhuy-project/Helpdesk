import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAccount, useAccountApi, useRoleAccess, useRoles } from '../hooks/use-accounts'
import { useAccountCommand } from '../hooks/use-account-command'
import { useAuth } from '../../auth/hooks/use-auth'
import { roleLabel } from '../../auth/lib/identity-labels'
import { AccountError, CommandFeedback } from '../components/account-state'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { PageTitle } from '../../../shared/components/page-title'
import { LoadingState } from '../../../shared/components/async-state'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import type { AccountUser, RoleDetails } from '../types/account'

export function RoleAssignmentPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <AssignmentDetails key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} />
    : <AccountError error={new ApiError('ID tài khoản không hợp lệ.', 'http', 404)} />
}
function AssignmentDetails({ id }: { id: string }) {
  const target = useAccount(id, true)
  const access = useRoleAccess()
  const roles = useRoles()
  const [revision, setRevision] = useState(0)
  const [saved, setSaved] = useState(false)
  const reload = async () => { await Promise.allSettled([target.refetch(), access.refetch(), roles.refetch()]); setRevision(n => n + 1) }
  if (target.error || access.error || roles.error) return <AccountError error={target.error ?? access.error ?? roles.error} onRetry={() => { void reload() }} />
  if (!target.data || !access.data || !roles.data) return <LoadingState />
  const value = target.data
  const allowed = roles.data.filter(role => access.data.assignableRoleIds.includes(role.id))
  const outside = value.roles.some(role => !allowed.some(item => item.id === role.id))
  return <section className="account-page"><PageTitle title={`Vai trò · ${value.displayName}`} /><p className="eyebrow">Cấp vai trò</p><h1>Vai trò của {value.displayName}</h1><p>{value.email}</p>
    <p><Link to="/admin/roles/assignments">Về danh sách cấp vai trò</Link></p>
    {saved && <p role="status" className="success-message">Đã cập nhật vai trò.</p>}
    {outside ? <p role="alert">Vai trò hiện tại nằm ngoài phạm vi cấp của bạn. Tải lại hoặc liên hệ quản trị hệ thống.</p>
      : <AssignmentForm key={`${value.version}:${revision}`} target={value} roles={allowed} onReload={() => { void reload() }} onSaved={() => { setSaved(true); void reload() }} />}
  </section>
}
function AssignmentForm({ target, roles, onSaved, onReload }: { target: AccountUser; roles: RoleDetails[]; onSaved: () => void; onReload: () => void }) {
  const { api } = useAccountApi()
  const command = useAccountCommand()
  const [selected, setSelected] = useState(target.roles.map(role => role.id))
  const [confirm, setConfirm] = useState(false)
  return <form className="panel account-form" onSubmit={event => { event.preventDefault(); setConfirm(true) }}>
    <h2>Vai trò được gán</h2><p>Lưu thay thế toàn bộ vai trò hiện tại. Không chọn vai trò nào sẽ gỡ mọi vai trò. Server kiểm tra lại phạm vi và bảo vệ quản trị viên cuối cùng.</p>
    <fieldset disabled={command.pending}><legend>Vai trò trong phạm vi cấp</legend>
      <div className="account-checks">{roles.map(role => <label className="account-check" key={role.id}>
        <input type="checkbox" checked={selected.includes(role.id)} onChange={event => setSelected(previous => event.target.checked ? [...previous, role.id] : previous.filter(id => id !== role.id))} />
        <span>{roleLabel(role.name)}<code>{role.name}</code></span>
      </label>)}</div>
    </fieldset>
    <CommandFeedback command={command} onReload={onReload} />
    <Button type="submit" disabled={command.disabled}>Xem lại và lưu vai trò</Button>
    <Dialog open={confirm} onOpenChange={open => { if (!command.pending) setConfirm(open) }} title="Xác nhận gán vai trò" description={`${target.displayName}: thay thế bằng ${selected.length} vai trò đã chọn.`}>
      <ul>{roles.filter(role => selected.includes(role.id)).map(role => <li key={role.id}>{roleLabel(role.name)}</li>)}</ul>
      {!selected.length && <p>Tài khoản sẽ không còn vai trò nào.</p>}
      <Button disabled={command.disabled} onClick={async () => {
        const result = await command.run(async signal => {
          const receipt = await api.setRoles(target.id, selected, target.version, signal)
          if (receipt?.id !== target.id || !receipt.version) throw new ApiError('Không xác nhận được vai trò đã lưu.', 'unexpected')
          return receipt
        })
        setConfirm(false)
        if (result) onSaved()
      }}>{command.pending ? 'Đang lưu…' : 'Xác nhận lưu vai trò'}</Button>
    </Dialog>
  </form>
}
