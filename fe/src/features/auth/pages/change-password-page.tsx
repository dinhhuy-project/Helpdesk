import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, getFieldError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'
import { FormField, Input } from '../../../shared/components/form-field'
import { CredentialError } from '../components/credential-error'
import { useAuth } from '../hooks/use-auth'
import { useCredentialForm } from '../hooks/use-credential-form'
import type { ChangePasswordRequest } from '../types/auth'
import { PageTitle } from '../../../shared/components/page-title'

export function ChangePasswordPage() {
  const { session } = useAuth()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const form = useCredentialForm(async (command: ChangePasswordRequest & { confirmation: string }) => {
    if (command.newPassword !== command.confirmation) {
      throw new ApiError('Vui lòng xác nhận lại mật khẩu mới.', 'http', 400, { errors: { confirmation: ['Mật khẩu xác nhận không khớp.'] } })
    }
    await session.changePassword({ currentPassword: command.currentPassword, newPassword: command.newPassword })
  })
  const currentError = form.error?.code === 'InvalidCurrentPassword' ? 'Mật khẩu hiện tại không đúng.'
    : form.error ? getFieldError(form.error, 'currentPassword') : undefined

  return (
    <section className="auth-panel" aria-labelledby="password-title">
      <PageTitle title="Đổi mật khẩu" />
      <p className="eyebrow">Bảo mật tài khoản</p><h1 id="password-title">Đổi mật khẩu</h1>
      <p className="auth-description">Sau khi đổi mật khẩu, mọi phiên đăng nhập sẽ kết thúc. Bạn cần đăng nhập lại.</p>
      <form aria-label="Đổi mật khẩu" onSubmit={event => {
        event.preventDefault()
        void form.submit({ currentPassword, newPassword, confirmation })
        setCurrentPassword(''); setNewPassword(''); setConfirmation('')
      }}>
        <FormField label="Mật khẩu hiện tại" required error={currentError}>
          {props => <Input {...props} type="password" autoComplete="current-password" maxLength={128}
            value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} disabled={form.pending} />}
        </FormField>
        <FormField label="Mật khẩu mới" required hint="Từ 12 đến 128 ký tự, khác mật khẩu hiện tại."
          error={form.error ? getFieldError(form.error, 'newPassword') : undefined}>
          {props => <Input {...props} type="password" autoComplete="new-password" minLength={12} maxLength={128}
            value={newPassword} onChange={event => setNewPassword(event.target.value)} disabled={form.pending} />}
        </FormField>
        <FormField label="Xác nhận mật khẩu mới" required error={form.error ? getFieldError(form.error, 'confirmation') : undefined}>
          {props => <Input {...props} type="password" autoComplete="new-password" minLength={12} maxLength={128}
            value={confirmation} onChange={event => setConfirmation(event.target.value)} disabled={form.pending} />}
        </FormField>
        <CredentialError error={form.error} remaining={form.remaining} />
        <Button type="submit" className="auth-submit" pending={form.pending} disabled={form.disabled}>Đổi mật khẩu</Button>
      </form>
      <Link className="auth-back" to="/account">Về tài khoản</Link>
    </section>
  )
}
