import type { TicketPriority, TicketStatus } from './ticket-enums'

export interface CreateTicketRequest {
  title: string
  description: string
  categoryId: string
  priority: TicketPriority
}
export interface TicketCreated {
  id: string
  ticketNumber: number
  requesterId: string
  createdByUserId: string
  categoryId: string
  status: TicketStatus
  priority: TicketPriority
  createdAt: string
  version: number
}

// Projections from TicketAccessModels.cs; details/access/activity are separate resources.
export interface TicketSummary {
  id: string
  ticketNumber: number
  title: string
  status: TicketStatus
  priority: TicketPriority
  requesterId: string
  categoryId: string
  assignedTeamId: string | null
  assignedAgentId: string | null
  createdAt: string
  updatedAt: string | null
  version: number
}
export interface TicketDetails extends TicketSummary {
  description: string
  resolutionSummary: string | null
  resolvedAt: string | null
  closedAt: string | null
}
export interface TicketAccess { canRead: boolean; canUpdateStatus: boolean; canAssign: boolean }
export interface TicketActivityData {
  ticketNumber: number | null
  requesterId: string | null
  createdByUserId: string | null
  categoryId: string | null
  status: string | null
  priority: string | null
  assignedTeamId: string | null
  assignedAgentId: string | null
  resolutionSummary: string | null
  resolvedAt: string | null
  closedAt: string | null
  commentId: string | null
  isInternal: boolean | null
  attachmentId: string | null
  dueAt: string | null
  detectedAt: string | null
}
export interface TicketActivity {
  id: string
  ticketId: string
  actorId: string | null
  action: string
  createdAt: string
  oldValue: TicketActivityData | null
  newValue: TicketActivityData | null
}
