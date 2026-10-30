import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, toApiError } from '../api/api-error'
import { useAuth } from '../../features/auth/hooks/use-auth'

/** Commands (including initial passwords) never enter the Query mutation cache. */
export function useApiCommand() {
  const { session, scope } = useAuth()
  const queries = useQueryClient()
  const [error, setError] = useState<ApiError | null>(null)
  const [pending, setPending] = useState(false)
  const [uncertain, setUncertain] = useState(false)
  const [remaining, setRemaining] = useState(0)
  const lock = useRef(false)
  const unknown = useRef(false)
  const until = useRef(0)
  const mounted = useRef(true)
  const controller = useRef<AbortController | null>(null)
  const isCurrent = () => mounted.current && session.store.getState().scope === scope
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false; controller.current?.abort() }
  }, [])
  useEffect(() => {
    if (!remaining) return
    const timer = setTimeout(() => setRemaining(Math.max(0, Math.ceil((until.current - Date.now()) / 1000))), 1000)
    return () => clearTimeout(timer)
  }, [remaining])
  async function run<T>(send: (signal: AbortSignal) => Promise<T>, afterCommit?: (receipt: T) => Promise<void> | void): Promise<T | undefined> {
    if (!scope || lock.current || unknown.current || Date.now() < until.current || error?.code === 'VersionConflict' || error?.status === 403 || error?.status === 404) return
    const current = isCurrent
    const abort = new AbortController()
    controller.current = abort; lock.current = true; setPending(true); setError(null)
    try {
      const receipt = await send(abort.signal)
      if (!current()) return
      // A committed write is never reported as failed because a later profile read failed.
      void queries.invalidateQueries({ refetchType: 'none' })
      try { await afterCommit?.(receipt) } catch { /* Profile boundary handles failed verification after commit. */ }
      return current() ? receipt : undefined
    } catch (failure) {
      if (!current()) return
      const normalized = toApiError(failure)
      setError(normalized)
      if (normalized.status === 403) void session.refreshProfile({ background: true }).catch(() => {})
      if (['network', 'timeout', 'cancelled', 'unexpected'].includes(normalized.kind) || (normalized.status ?? 0) >= 500) {
        unknown.current = true; setUncertain(true)
      }
      if (normalized.status === 429) {
        const seconds = normalized.retryAfterSeconds ?? 60
        until.current = Date.now() + seconds * 1000; setRemaining(seconds)
      }
      return
    } finally {
      lock.current = false
      if (controller.current === abort) controller.current = null
      if (current()) setPending(false)
    }
  }
  // Only call after an explicit, successful resource/access read and user review. Unknown writes stay blocked.
  function resetAfterVerification() {
    if (!isCurrent() || lock.current || unknown.current || Date.now() < until.current) return false
    setError(null)
    return true
  }
  return { run, error, pending, uncertain, remaining, isCurrent, resetAfterVerification,
    disabled: pending || uncertain || remaining > 0 || error?.code === 'VersionConflict' || error?.status === 403 || error?.status === 404 }
}
