import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { ApiError, toApiError } from '../../../shared/api/api-error'
import { createTicketApi } from '../api/ticket-api'
import { isTicketId } from '../lib/ticket-search'
import type { CommentDraft } from '../types/ticket-discussion'
import { ticketKeys } from './use-tickets'

export function useAddComment(id: string, onCreated: () => void, onDenied: (error: ApiError) => void) {
  const { client, session, scope, user } = useAuth()
  const queries = useQueryClient()
  const [error, setError] = useState<ApiError | null>(null)
  const [pending, setPending] = useState(false)
  const [uncertain, setUncertain] = useState(false)
  const [remaining, setRemaining] = useState(0)
  const [blockedUntil, setBlockedUntil] = useState(0)
  const busy = useRef(false)
  const unknownResult = useRef(false)
  const deadline = useRef(0)
  const mounted = useRef(true)
  const request = useRef<AbortController | null>(null)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false; request.current?.abort() }
  }, [])
  useEffect(() => {
    if (!blockedUntil) return
    const timer = setInterval(() => {
      const seconds = Math.max(0, Math.ceil((blockedUntil - Date.now()) / 1000))
      setRemaining(seconds)
      if (!seconds) clearInterval(timer)
    }, 1000)
    return () => clearInterval(timer)
  }, [blockedUntil])
  async function submit(value: CommentDraft) {
    if (!scope || !user || busy.current || unknownResult.current || Date.now() < deadline.current) return
    busy.current = true
    const currentScope = scope
    const current = () => mounted.current && session.store.getState().scope === currentScope
    const controller = new AbortController()
    request.current = controller
    setPending(true); setError(null)
    try {
      const receipt = await createTicketApi(client).comment(id, value, controller.signal)
      if (!isTicketId(receipt?.id) || receipt.ticketId !== id || receipt.authorId !== user.id || receipt.isInternal !== value.isInternal) {
        throw new ApiError('Không xác nhận được bình luận đã gửi.', 'unexpected')
      }
    } catch (failure) {
      if (current()) {
        const normalized = toApiError(failure)
        setError(normalized)
        if (['network', 'timeout', 'cancelled', 'unexpected'].includes(normalized.kind) || (normalized.status ?? 0) >= 500) {
          unknownResult.current = true; setUncertain(true)
        }
        if (normalized.status === 403 || normalized.status === 404) onDenied(normalized)
        if (normalized.status === 429) {
          const seconds = normalized.retryAfterSeconds ?? 60
          deadline.current = Date.now() + seconds * 1000; setBlockedUntil(deadline.current); setRemaining(seconds)
        }
      }
      return
    } finally {
      busy.current = false
      if (request.current === controller) request.current = null
      if (current()) setPending(false)
    }
    if (!current()) return
    // A confirmed append stays successful even if later reads fail. Never replay POST.
    void queries.invalidateQueries({ queryKey: ticketKeys.resource(currentScope, id), refetchType: 'none' })
    void queries.invalidateQueries({ queryKey: ticketKeys.lists(currentScope), refetchType: 'none' })
    onCreated()
  }
  return { submit, pending, uncertain, remaining, error, disabled: pending || uncertain || remaining > 0,
    allowResubmit: () => { unknownResult.current = false; setUncertain(false); setError(null) } }
}
