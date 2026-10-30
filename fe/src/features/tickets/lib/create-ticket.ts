import { isTicketId } from './ticket-search'
import type { CreateTicketRequest } from '../types/ticket'
import type { TicketPriority } from '../types/ticket-enums'

export interface TicketDraft { title: string; description: string; categoryId: string; priority: string }
export type TicketDraftErrors = Partial<Record<keyof TicketDraft, string>>
export function validateTicketDraft(draft: TicketDraft): { value: CreateTicketRequest; errors?: never } | { errors: TicketDraftErrors; value?: never } {
  const errors: TicketDraftErrors = {}
  if (!draft.title.trim()) errors.title = 'Vui lòng nhập tiêu đề.'
  else if (draft.title.length > 300) errors.title = 'Tiêu đề tối đa 300 ký tự.'
  if (!draft.description.trim()) errors.description = 'Vui lòng nhập nội dung yêu cầu.'
  else if (draft.description.length > 20_000) errors.description = 'Nội dung tối đa 20.000 ký tự.'
  if (!isTicketId(draft.categoryId)) errors.categoryId = 'Vui lòng chọn danh mục khả dụng.'
  if (!/^[1-4]$/.test(draft.priority)) errors.priority = 'Độ ưu tiên không hợp lệ.'
  if (Object.keys(errors).length) return { errors }
  return { value: { title: draft.title.trim(), description: draft.description.trim(), categoryId: draft.categoryId,
    priority: Number(draft.priority) as TicketPriority } }
}
