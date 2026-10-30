import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import type { SlaPage, SlaPolicy, SlaPolicyInput, TicketSla } from '../types/sla'

const root = '/api/v1/admin/sla-policies'
function policyPath(id: string) {
  if (!isGuid(id)) throw new Error('Mã policy không hợp lệ.')
  return `${root}/${id}`
}
function body(value: SlaPolicyInput): SlaPolicyInput {
  return { name: value.name, priority: value.priority, firstResponseMinutes: value.firstResponseMinutes, resolutionMinutes: value.resolutionMinutes }
}
export function verifySlaPolicy(value: SlaPolicy, id?: string) {
  if (!value || !isGuid(value.id) || (id !== undefined && value.id !== id) || !Number.isInteger(value.version) || value.version <= 0 || typeof value.isActive !== 'boolean') {
    throw new ApiError('Không xác nhận được kết quả policy SLA.', 'unexpected')
  }
  return value
}
export function createSlaApi(client: HttpClient) {
  return {
    list: (search: SlaPage, signal: AbortSignal) => client.get<PagedResult<SlaPolicy>>(root, { params: search, signal }),
    detail: async (id: string, signal: AbortSignal) => verifySlaPolicy(await client.get<SlaPolicy>(policyPath(id), { signal }), id),
    create: async (value: SlaPolicyInput, signal: AbortSignal) => verifySlaPolicy(await client.post<SlaPolicy, SlaPolicyInput>(root, body(value), { signal })),
    update: async (id: string, value: SlaPolicyInput, version: number, signal: AbortSignal) => verifySlaPolicy(
      await client.patch<SlaPolicy>(policyPath(id), { ...body(value), version }, { signal }), id),
    status: async (id: string, isActive: boolean, version: number, signal: AbortSignal) => verifySlaPolicy(
      await client.put<SlaPolicy>(`${policyPath(id)}/status`, { isActive, version }, { signal }), id),
    ticket: async (id: string, signal: AbortSignal) => {
      if (!isGuid(id)) throw new Error('Mã Ticket không hợp lệ.')
      const value = await client.get<TicketSla>(`/api/v1/tickets/${id}/sla`, { signal })
      if (!value || value.ticketId !== id || !Object.hasOwn(value, 'sla') || !Number.isFinite(Date.parse(value.asOf)) || ![1, 2, 3, 4, 5, 6, 7].includes(value.status)) {
        throw new ApiError('Dữ liệu SLA không khớp yêu cầu.', 'unexpected')
      }
      return value
    },
  }
}
