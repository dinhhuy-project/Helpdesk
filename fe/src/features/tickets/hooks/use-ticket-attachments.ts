import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, toApiError } from '../../../shared/api/api-error'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { useAuth } from '../../auth/hooks/use-auth'
import { createTicketAttachmentApi } from '../api/ticket-attachment-api'
import { attachmentType, saveAttachmentBlob, validateAttachment } from '../lib/attachment-file'
import type { AttachmentOptions, AttachmentSearch, AttachmentTarget, TicketAttachment } from '../types/ticket-attachment'
import { ticketKeys } from './use-tickets'

const sensitive = { staleTime: 0, gcTime: 0, refetchOnMount: 'always' as const }
export function useAttachmentOptions(id: string, commentId: string | null) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: [...ticketKeys.resource(scope, id), 'attachment-options', commentId],
    queryFn: ({ signal }) => createTicketAttachmentApi(client).options(id, commentId, signal) })
}
export function useAttachmentList(id: string, search: AttachmentSearch) {
  const { client, scope } = useAuth()
  return useQuery({ ...sensitive, queryKey: [...ticketKeys.resource(scope, id), 'attachments', search],
    queryFn: ({ signal }) => createTicketAttachmentApi(client).list(id, search, signal) })
}

// Lives at the Ticket boundary: revalidating capabilities/detail never loses an in-flight result or File draft.
export function useTicketAttachmentUpload(id: string, onDenied: () => void) {
  const { client, scope, user } = useAuth()
  const queries = useQueryClient()
  const command = useApiCommand()
  const [selection, setSelection] = useState<{ file: File; target: AttachmentTarget } | null>(null)
  const selected = useRef(selection)
  const [progress, setProgress] = useState(0)
  const [validation, setValidation] = useState<string>()
  const [success, setSuccess] = useState('')
  const abort = useRef<AbortController | null>(null)
  useEffect(() => () => { abort.current?.abort(); selected.current = null }, [])
  function choose(file: File | null, target: AttachmentTarget) {
    if (command.disabled) return
    selected.current = file ? { file, target } : null
    setSelection(selected.current); setValidation(undefined); setSuccess(''); setProgress(0)
  }
  function forgetInternal() {
    if (!selected.current?.target.isInternal) return
    abort.current?.abort(); selected.current = null; setSelection(null); setValidation(undefined)
  }
  async function submit(target: AttachmentTarget, options: AttachmentOptions) {
    const value = selected.current
    if (!value || value.target.commentId !== target.commentId || value.target.isInternal !== target.isInternal || command.disabled ||
      !(target.isInternal ? options.access.canUploadInternal : options.access.canUploadPublic)) return
    const invalid = validateAttachment(value.file, options.policy)
    setValidation(invalid)
    if (invalid) return
    setSuccess(''); setProgress(0)
    const controller = new AbortController(); abort.current = controller
    await command.run(async signal => {
      try {
        const result = await createTicketAttachmentApi(client).upload(id, target, value.file, attachmentType(value.file, options.policy)!,
          AbortSignal.any([signal, controller.signal]), percent => { if (command.isCurrent() && !controller.signal.aborted) setProgress(percent) })
        if (result.uploadedByUserId !== user?.id) throw new ApiError('Không xác nhận được người tải file.', 'unexpected')
        return result
      } catch (failure) {
        const error = toApiError(failure)
        if (command.isCurrent() && (error.status === 403 || error.status === 404)) onDenied()
        throw error
      }
    }, () => {
      selected.current = null; setSelection(null); setSuccess('Đã tải file lên.'); setProgress(0)
      void queries.invalidateQueries({ queryKey: ticketKeys.resource(scope, id),
        predicate: query => query.queryKey[3] === 'attachments' || query.queryKey[3] === 'activities' })
    })
    if (abort.current === controller) abort.current = null
  }
  return { command, selection, progress, validation, success, choose, submit, forgetInternal }
}
export type AttachmentUpload = ReturnType<typeof useTicketAttachmentUpload>

export function useAttachmentDownload(id: string, target: AttachmentTarget, onDenied: () => void) {
  const { client, scope, session } = useAuth()
  const [pending, setPending] = useState<string | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [remaining, setRemaining] = useState(0)
  const locked = useRef(false), alive = useRef(true), deadline = useRef(0)
  const controller = useRef<AbortController | null>(null)
  useEffect(() => { alive.current = true; return () => { alive.current = false; controller.current?.abort() } }, [])
  useEffect(() => {
    if (!remaining) return
    const timer = setTimeout(() => setRemaining(Math.max(0, Math.ceil((deadline.current - Date.now()) / 1000))), 1000)
    return () => clearTimeout(timer)
  }, [remaining])
  async function download(file: TicketAttachment) {
    if (locked.current || Date.now() < deadline.current) return
    locked.current = true
    const abort = new AbortController(); controller.current = abort
    const current = () => alive.current && !abort.signal.aborted && session.store.getState().scope === scope
    setError(null); setPending(file.id)
    try {
      const api = createTicketAttachmentApi(client)
      const metadata = await api.metadata(id, file.id, target, abort.signal)
      const blob = await api.download(id, metadata, abort.signal)
      if (current()) saveAttachmentBlob(blob, metadata.fileName)
    } catch (failure) {
      if (current()) {
        const normalized = toApiError(failure); setError(normalized)
        if (normalized.status === 403 || normalized.status === 404) onDenied()
        if (normalized.status === 429) { const seconds = normalized.retryAfterSeconds ?? 60; deadline.current = Date.now() + seconds * 1000; setRemaining(seconds) }
      }
    } finally { locked.current = false; if (current()) setPending(null) }
  }
  return { download, pending, error, remaining }
}
