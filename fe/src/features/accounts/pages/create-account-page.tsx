import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { useAccountApi } from '../hooks/use-accounts'
import { useAccountCommand } from '../hooks/use-account-command'
import { CommandFeedback } from '../components/account-state'
import { PageTitle } from '../../../shared/components/page-title'
import { FormField, Input } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { ApiError, getFieldError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'

export function CreateAccountPage() {
  const { scope } = useAuth()
  return <CreateAccountForm key={scope} />
}
function CreateAccountForm() {
  const { api } = useAccountApi()
  const command = useAccountCommand()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  return <section className="account-page"><PageTitle title="Tạo tài khoản" />
    <p className="eyebrow">Quản trị tài khoản</p><h1>Tạo tài khoản</h1>
    <p>Tài khoản mới hoạt động với vai trò Người yêu cầu. Sau khi tạo, bạn có thể gán phòng ban và quản lý vai trò theo quyền hiện tại.</p>
    <form className="panel account-form" onSubmit={async event => {
      event.preventDefault()
      if (!email.trim() || !displayName.trim()) return
      const result = await command.run(async signal => {
        const receipt = await api.create({ email: email.trim(), displayName: displayName.trim(), password }, signal)
        if (!isGuid(receipt?.id) || !receipt.version) throw new ApiError('Không xác nhận được tài khoản đã tạo.', 'unexpected')
        return receipt
      })
      if (result) { setPassword(''); void navigate(`/admin/users/${result.id}`, { replace: true, state: { accountCreated: true } }) }
    }}>
      <FormField label="Email" required error={getFieldError(command.error, 'email')}>{props => <Input {...props} type="email" maxLength={320} autoComplete="off" value={email} disabled={command.pending} onChange={event => setEmail(event.target.value)} />}</FormField>
      <FormField label="Tên hiển thị" required error={getFieldError(command.error, 'displayName')}>{props => <Input {...props} maxLength={200} value={displayName} disabled={command.pending} onChange={event => setDisplayName(event.target.value)} />}</FormField>
      <FormField label="Mật khẩu ban đầu" required hint="Từ 12 đến 128 ký tự. Chỉ dùng cho tài khoản mới." error={getFieldError(command.error, 'password')}>{props => <Input {...props} type="password" minLength={12} maxLength={128} autoComplete="new-password" value={password} disabled={command.pending} onChange={event => setPassword(event.target.value)} />}</FormField>
      <CommandFeedback command={command} create />
      <div className="account-actions"><Button type="submit" disabled={command.disabled || !displayName.trim() || password.length < 12}>{command.pending ? 'Đang tạo…' : 'Tạo tài khoản'}</Button><Link to="/admin/users">Về danh sách</Link></div>
    </form>
  </section>
}
