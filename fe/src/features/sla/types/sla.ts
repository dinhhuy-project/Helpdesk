import type { TicketPriority, TicketStatus } from '../../tickets/types/ticket-enums'

export interface SlaPolicyInput { name: string; priority: TicketPriority; firstResponseMinutes: number; resolutionMinutes: number }
export interface SlaPolicy extends SlaPolicyInput { id: string; isActive: boolean; createdAt: string; updatedAt: string | null; version: number }
export interface SlaPage { page: number; pageSize: number }
export interface TicketSlaSnapshot {
  policyId: string; policyName: string | null; priority: TicketPriority | null
  firstResponseMinutes: number | null; resolutionMinutes: number | null
  appliedAt: string; firstResponseDueAt: string; resolutionDueAt: string
  firstRespondedAt: string | null; resolvedAt: string | null
  firstResponseBreached: boolean; resolutionBreached: boolean
  firstResponseBreachedAt: string | null; resolutionBreachedAt: string | null
}
export interface TicketSla { ticketId: string; sla: TicketSlaSnapshot | null; status: TicketStatus; asOf: string }
