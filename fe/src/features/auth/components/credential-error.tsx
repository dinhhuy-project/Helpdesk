import type { ApiError } from '../../../shared/api/api-error'

export function CredentialError({ error, remaining }: { error: ApiError | null; remaining: number }) {
  if (!error) return null
  return (
    <div className="auth-message auth-message--error" role="alert">
      <p>{error.message}</p>
      {remaining > 0 && <p>Vui lòng chờ {remaining} giây trước khi thử lại.</p>}
      {error.traceId && <p className="field-hint">Mã tra cứu: {error.traceId}</p>}
    </div>
  )
}
