import { useEffect, useRef, useState } from 'react'
import { ApiError, toApiError } from '../../../shared/api/api-error'

/** Sensitive form values go directly to the command, never through the Query mutation cache. */
export function useCredentialForm<T>(command: (value: T) => Promise<void>) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [blockedUntil, setBlockedUntil] = useState(0)
  const [remaining, setRemaining] = useState(0)
  const busy = useRef(false)
  const blockedDeadline = useRef(0)
  const mounted = useRef(true)

  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    if (!blockedUntil) return
    const update = () => {
      const seconds = Math.max(0, Math.ceil((blockedUntil - Date.now()) / 1000))
      setRemaining(seconds)
      if (!seconds) clearInterval(timer)
    }
    const timer = setInterval(update, 1000)
    return () => clearInterval(timer)
  }, [blockedUntil])

  async function submit(value: T) {
    if (busy.current || Date.now() < blockedDeadline.current) return
    busy.current = true
    setPending(true)
    setError(null)
    try {
      await command(value)
    } catch (failure) {
      if (mounted.current) {
        const normalized = toApiError(failure)
        if (normalized.kind !== 'cancelled') setError(normalized)
        if (normalized.status === 429) {
          const seconds = normalized.retryAfterSeconds ?? 60
          blockedDeadline.current = Date.now() + seconds * 1000
          setBlockedUntil(blockedDeadline.current)
          setRemaining(seconds)
        }
      }
    } finally {
      busy.current = false
      if (mounted.current) setPending(false)
    }
  }
  return { submit, pending, error, remaining, disabled: pending || remaining > 0 }
}
