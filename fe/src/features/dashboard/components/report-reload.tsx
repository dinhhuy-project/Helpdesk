import { useEffect, useState } from 'react'
import { Button } from '../../../shared/components/button'

export function ReportReload({ pending, retryAt, reload }: { pending: boolean; retryAt: number; reload: () => void }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!retryAt) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [retryAt])
  const remaining = Math.max(0, Math.ceil((retryAt - now) / 1000))
  return <Button variant="secondary" disabled={pending || remaining > 0} onClick={reload}>Tải lại số liệu{remaining > 0 && ` (${remaining}s)`}</Button>
}
