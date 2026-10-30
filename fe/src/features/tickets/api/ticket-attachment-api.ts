import { ApiError } from '../../../shared/api/api-error'
import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import { isGuid } from '../../../shared/lib/identifier'
import type { AttachmentOptions, AttachmentSearch, AttachmentTarget, TicketAttachment } from '../types/ticket-attachment'

function path(ticketId: string, attachmentId?: string) {
  if (!isGuid(ticketId) || (attachmentId !== undefined && !isGuid(attachmentId))) throw new Error('Mã file hoặc yêu cầu không hợp lệ.')
  return `/api/v1/tickets/${ticketId}/attachments${attachmentId ? `/${attachmentId}` : ''}`
}
function project(value: TicketAttachment, ticketId: string, target: AttachmentTarget) {
  if (!value || !isGuid(value.id) || value.ticketId !== ticketId || value.isInternal !== target.isInternal ||
    (target.commentId !== null && value.commentId !== target.commentId) || !isGuid(value.uploadedByUserId) ||
    !Number.isSafeInteger(value.sizeBytes) || value.sizeBytes <= 0 || typeof value.fileName !== 'string' || typeof value.contentType !== 'string') {
    throw new ApiError('Dữ liệu file không đúng phạm vi yêu cầu.', 'unexpected')
  }
  // Whitelist the public DTO. Storage coordinates/URLs never enter the cache or DOM.
  return { id: value.id, ticketId: value.ticketId, commentId: value.commentId, uploadedByUserId: value.uploadedByUserId,
    fileName: value.fileName, contentType: value.contentType, sizeBytes: value.sizeBytes, isInternal: value.isInternal, createdAt: value.createdAt }
}
export function createTicketAttachmentApi(client: HttpClient) {
  return {
    options: async (ticketId: string, commentId: string | null, signal: AbortSignal) => {
      if (commentId !== null && !isGuid(commentId)) throw new Error('Mã bình luận không hợp lệ.')
      const value = await client.get<AttachmentOptions>(`${path(ticketId)}/options`, { params: { commentId: commentId ?? undefined }, signal })
      if (!value?.access || !value.policy || !Number.isSafeInteger(value.policy.maxFileSizeBytes) || value.policy.maxFileSizeBytes <= 0 || !Array.isArray(value.policy.fileTypes)) {
        throw new ApiError('Không xác nhận được giới hạn file hiện tại.', 'unexpected')
      }
      return value
    },
    list: async (ticketId: string, search: AttachmentSearch, signal: AbortSignal) => {
      const result = await client.get<PagedResult<TicketAttachment>>(path(ticketId), { params: { ...search, commentId: search.commentId ?? undefined }, signal })
      return { ...result, items: result.items.map(value => project(value, ticketId, search)) }
    },
    metadata: async (ticketId: string, attachmentId: string, target: AttachmentTarget, signal: AbortSignal) => {
      const value = project(await client.get<TicketAttachment>(path(ticketId, attachmentId), { signal }), ticketId, target)
      if (value.id !== attachmentId) throw new ApiError('Mã file không khớp yêu cầu.', 'unexpected')
      return value
    },
    upload: async (ticketId: string, target: AttachmentTarget, file: File, contentType: string, signal: AbortSignal, onProgress: (percent: number) => void) => {
      if (target.commentId !== null && !isGuid(target.commentId)) throw new Error('Mã bình luận không hợp lệ.')
      const form = new FormData()
      form.append('file', new Blob([file], { type: contentType }), file.name)
      if (target.commentId) form.append('commentId', target.commentId)
      else form.append('isInternal', String(target.isInternal))
      // Browser sets the multipart boundary. 100% sent is not a committed upload receipt.
      const value = await client.post<TicketAttachment, FormData>(path(ticketId), form, { signal, timeout: 150_000,
        onUploadProgress: event => { if (!signal.aborted && event.total) onProgress(Math.min(100, Math.round(event.loaded * 100 / event.total))) } })
      const result = project(value, ticketId, target)
      if (result.commentId !== target.commentId) throw new ApiError('File đã nhận không khớp bình luận.', 'unexpected')
      return result
    },
    download: async (ticketId: string, metadata: TicketAttachment, signal: AbortSignal) => {
      const data = await client.get<ArrayBuffer>(`${path(ticketId, metadata.id)}/download`, { responseType: 'arraybuffer', timeout: 150_000, signal })
      const blob = new Blob([data], { type: metadata.contentType })
      if (blob.size !== metadata.sizeBytes) throw new ApiError('File tải về chưa đầy đủ. Hãy thử tải lại.', 'unexpected')
      return blob
    },
  }
}
