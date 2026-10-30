import type { TicketStatus } from './ticket-enums'

export interface TicketWorkflowContext {
  version: number
  status: TicketStatus
  canAssign: boolean
  assignmentIsValid: boolean
  transitions: TicketStatus[]
  assignedTeamId: string | null
  assignedTeamName: string | null
  assignedAgentId: string | null
  assignedAgentName: string | null
}
export interface AssignmentTeam { id: string; name: string; departmentName: string }
export interface AssignmentAgent { id: string; displayName: string }
export interface AssignmentSearch { page: number; pageSize: number; search: string }
export interface AssignTicketRequest { teamId: string; agentId: string | null; version: number }
export interface TransitionTicketRequest { status: TicketStatus; resolutionSummary: string | null; version: number }
export interface TicketMutationResult {
  id: string; status: TicketStatus; assignedTeamId: string | null; assignedAgentId: string | null
  updatedAt: string | null; resolvedAt: string | null; closedAt: string | null; version: number
}
// Local draft survives access/detail revalidation. A fresh server version is accepted only by explicit review.
export interface WorkflowDraft {
  version: number
  team: AssignmentTeam | null
  agent: AssignmentAgent | null
  agentChosen: boolean
  assignmentReviewed: boolean
  status: TicketStatus | null
  resolutionSummary: string
}
export function emptyWorkflowDraft(version: number): WorkflowDraft {
  return { version, team: null, agent: null, agentChosen: false, assignmentReviewed: false, status: null, resolutionSummary: '' }
}
