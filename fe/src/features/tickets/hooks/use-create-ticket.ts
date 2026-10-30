import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { ApiError, toApiError } from '../../../shared/api/api-error'
import { createTicketApi } from '../api/ticket-api'
import { isTicketId } from '../lib/ticket-search'
import type { CreateTicketRequest, TicketCreated } from '../types/ticket'
import { categoryLookupKeys } from './use-ticket-category-lookup'
import { ticketKeys } from './use-tickets'

export function useCreateTicket({ onCreated, onInvalidCategory }: {
  onCreated: (receipt: TicketCreated) => void
  onInvalidCategory: () => void
}) {
  const { client, session, scope } = useAuth()
  const queries = useQueryClient()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [uncertain, setUncertain] = useState(false)
  const [remaining, setRemaining] = useState(0)
  const [blockedUntil, setBlockedUntil] = useState(0)
  const deadline = useRef(0)
  const busy = useRef(false)
  const uncertainResult = useRef(false)
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

  async function submit(value: CreateTicketRequest) {
    if (!scope || busy.current || uncertainResult.current || Date.now() < deadline.current) return
    const currentScope = scope
    const current = () => mounted.current && session.store.getState().scope === currentScope
    const controller = new AbortController()
    request.current = controller
    busy.current = true
    setPending(true)
    setError(null)
    let receipt: TicketCreated | undefined
    try {
      // Direct command: no Query mutation retry, background retry or persisted draft/body.
      receipt = await createTicketApi(client).create(value, controller.signal)
      if (!isTicketId(receipt?.id)) throw new ApiError('Không xác nhận được mã yêu cầu đã tạo.', 'unexpected')
    } catch (failure) {
      if (current()) {
        const normalized = toApiError(failure)
        setError(normalized)
        const unknown = ['network', 'timeout', 'cancelled', 'unexpected'].includes(normalized.kind)
          || (normalized.status !== undefined && normalized.status >= 500)
        if (unknown) {
          uncertainResult.current = true
          setUncertain(true)
          void queries.invalidateQueries({ queryKey: ticketKeys.lists(currentScope), refetchType: 'none' })
        }
        if (normalized.code === 'InvalidCategory') {
          onInvalidCategory()
          void queries.invalidateQueries({ queryKey: categoryLookupKeys.scope(currentScope) })
        }
        if (normalized.status === 429) {
          const seconds = normalized.retryAfterSeconds ?? 60
          deadline.current = Date.now() + seconds * 1000
          setBlockedUntil(deadline.current)
          setRemaining(seconds)
        }
      }
      return
    } finally {
      busy.current = false
      if (request.current === controller) request.current = null
      if (current()) setPending(false)
    }
    if (!current() || !receipt) return
    // Invalidation must not turn a confirmed 201 into a failed command or resubmit its POST.
    void queries.invalidateQueries({ queryKey: ticketKeys.lists(currentScope), refetchType: 'none' })
    onCreated(receipt)
  }

  function allowResubmit() {
    if (busy.current) return
    uncertainResult.current = false
    setUncertain(false)
    setError(null)
  }
  return { submit, pending, error, uncertain, remaining, allowResubmit,
    disabled: pending || uncertain || remaining > 0 }
}
