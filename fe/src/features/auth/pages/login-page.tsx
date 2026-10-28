import { useState } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { getFieldError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'
import { FormField, Input } from '../../../shared/components/form-field'
import { CredentialError } from '../components/credential-error'
import { SessionNotice } from '../components/session-notice'
import { useAuth } from '../hooks/use-auth'
import { useCredentialForm } from '../hooks/use-credential-form'
import { safeReturnTo } from '../lib/return-to'
import { PageTitle } from '../../../shared/components/page-title'

export function LoginPage() {
  const { session, status } = useAuth()
  const [params] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const form = useCredentialForm(session.login)
  if (status === 'authenticated') return <Navigate to={safeReturnTo(params.get('returnTo'))} replace />

  return (
    <section className="auth-panel" aria-labelledby="login-title">
      <PageTitle title="Đăng nhập" />
      <p className="eyebrow">Tài khoản Helpdesk</p><h1 id="login-title">Đăng nhập</h1>
      <p className="auth-description">Đăng nhập bằng tài khoản do doanh nghiệp cung cấp.</p>
      <SessionNotice />
      <form aria-label="Đăng nhập" onSubmit={event => {
        event.preventDefault()
        void form.submit({ email: email.trim(), password })
        setPassword('')
      }}>
        <FormField label="Email" required error={form.error ? getFieldError(form.error, 'email') : undefined}>
          {props => <Input {...props} type="email" autoComplete="username" maxLength={320}
            value={email} onChange={event => setEmail(event.target.value)} disabled={form.pending} />}
        </FormField>
        <FormField label="Mật khẩu" required error={form.error ? getFieldError(form.error, 'password') : undefined}>
          {props => <Input {...props} type="password" autoComplete="current-password" maxLength={128}
            value={password} onChange={event => setPassword(event.target.value)} disabled={form.pending} />}
        </FormField>
        <CredentialError error={form.error} remaining={form.remaining} />
        <Button type="submit" className="auth-submit" pending={form.pending} disabled={form.disabled}>
          {form.pending ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </Button>
      </form>
      <p className="field-hint auth-footnote">Bạn cần đăng nhập lại khi tải lại hoặc mở lại trang.</p>
    </section>
  )
}
