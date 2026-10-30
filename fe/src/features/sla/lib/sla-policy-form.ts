import type { TicketPriority } from '../../tickets/types/ticket-enums'
import type { SlaPage, SlaPolicyInput } from '../types/sla'

export interface SlaPolicyDraft { name: string; priority: string; firstResponseMinutes: string; resolutionMinutes: string }
export const slaMaximumMinutes = 525_600
export const emptySlaDraft: SlaPolicyDraft = { name: '', priority: '2', firstResponseMinutes: '', resolutionMinutes: '' }
export function policyDraft(value: SlaPolicyInput): SlaPolicyDraft {
  return { name: value.name, priority: String(value.priority), firstResponseMinutes: String(value.firstResponseMinutes), resolutionMinutes: String(value.resolutionMinutes) }
}
export function parsePolicyDraft(value: SlaPolicyDraft): { value?: SlaPolicyInput; errors: Partial<Record<keyof SlaPolicyDraft, string>> } {
  const errors: Partial<Record<keyof SlaPolicyDraft, string>> = {}
  if (!value.name.trim() || value.name.length > 200) errors.name = 'Nhập tên policy, tối đa 200 ký tự.'
  if (!['1', '2', '3', '4'].includes(value.priority)) errors.priority = 'Chọn mức ưu tiên hợp lệ.'
  for (const field of ['firstResponseMinutes', 'resolutionMinutes'] as const) {
    if (!/^\d+$/.test(value[field]) || Number(value[field]) < 1 || Number(value[field]) > slaMaximumMinutes) errors[field] = 'Nhập số phút nguyên từ 1 đến 525.600.'
  }
  if (!errors.firstResponseMinutes && !errors.resolutionMinutes && Number(value.firstResponseMinutes) > Number(value.resolutionMinutes)) errors.resolutionMinutes = 'Mục tiêu giải quyết phải lớn hơn hoặc bằng mục tiêu phản hồi đầu tiên.'
  return Object.keys(errors).length ? { errors } : { errors, value: { name: value.name.trim(), priority: Number(value.priority) as TicketPriority,
    firstResponseMinutes: Number(value.firstResponseMinutes), resolutionMinutes: Number(value.resolutionMinutes) } }
}
export function parseSlaPage(params: URLSearchParams): { value?: SlaPage; error?: string } {
  const read = (name: string, fallback: number, max: number) => {
    const raw = params.get(name)
    if (params.getAll(name).length > 1 || (raw !== null && (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > max))) throw new Error('Phân trang SLA không hợp lệ.')
    return raw === null ? fallback : Number(raw)
  }
  try { return { value: { page: read('page', 1, 1_000_000), pageSize: read('pageSize', 20, 100) } } }
  catch { return { error: 'Phân trang SLA không hợp lệ.' } }
}
