import type { HttpClient } from '../../../shared/api/http-client'
import type { PagedResult } from '../../../shared/api/pagination'
import type { TicketSearch } from '../lib/ticket-search'
import { isTicketId } from '../lib/ticket-search'
import type { CreateTicketRequest, TicketCreated, TicketAccess, TicketActivity, TicketDetails, TicketSummary } from '../types/ticket'
import type { CommentDraft, CommentSearch, TicketComment, TicketDiscussionAccess } from '../types/ticket-discussion'
import { ApiError } from '../../../shared/api/api-error'
import type { AssignTicketRequest, AssignmentAgent, AssignmentSearch, AssignmentTeam, TicketMutationResult, TicketWorkflowContext, TransitionTicketRequest } from '../types/ticket-workflow'

const root = '/api/v1/tickets'
function ticketPath(id: string) {
  if (!isTicketId(id)) throw new Error('Mã định danh yêu cầu không hợp lệ.')
  return `${root}/${id}`
}
export function createTicketApi(client: HttpClient) {
  return {
    // Construct only the four accepted fields; requester/creator/number come from the server.
    create: (value: CreateTicketRequest, signal: AbortSignal) => client.post<TicketCreated, CreateTicketRequest>(root,
      { title: value.title, description: value.description, categoryId: value.categoryId, priority: value.priority }, { signal }),
    list: (search: TicketSearch, signal: AbortSignal) => client.get<PagedResult<TicketSummary>>(root, { params: search, signal }),
    detail: (id: string, signal: AbortSignal) => client.get<TicketDetails>(ticketPath(id), { signal }),
    access: (id: string, signal: AbortSignal) => client.get<TicketAccess>(`${ticketPath(id)}/access`, { signal }),
    workflow: async (id: string, signal: AbortSignal) => {
      const value = await client.get<TicketWorkflowContext>(`${ticketPath(id)}/workflow`, { signal })
      if (!Number.isInteger(value.version) || value.version <= 0 || !Array.isArray(value.transitions) || typeof value.canAssign !== 'boolean') {
        throw new ApiError('Không xác nhận được thao tác hiện tại của yêu cầu.', 'unexpected')
      }
      return value
    },
    assignmentTeams: (id: string, search: AssignmentSearch, signal: AbortSignal) => client.get<PagedResult<AssignmentTeam>>(`${ticketPath(id)}/assignment-teams`, { params: search, signal }),
    assignmentAgents: (id: string, teamId: string, search: AssignmentSearch, signal: AbortSignal) => {
      if (!isTicketId(teamId)) throw new Error('Mã nhóm không hợp lệ.')
      return client.get<PagedResult<AssignmentAgent>>(`${ticketPath(id)}/assignment-teams/${teamId}/agents`, { params: search, signal })
    },
    assign: (id: string, value: AssignTicketRequest, signal: AbortSignal) => client.post<TicketMutationResult, AssignTicketRequest>(`${ticketPath(id)}/assign`,
      { teamId: value.teamId, agentId: value.agentId, version: value.version }, { signal }),
    transition: (id: string, value: TransitionTicketRequest, signal: AbortSignal) => client.post<TicketMutationResult, TransitionTicketRequest>(`${ticketPath(id)}/transition`,
      { status: value.status, resolutionSummary: value.resolutionSummary, version: value.version }, { signal }),
    discussionAccess: (id: string, signal: AbortSignal) => client.get<TicketDiscussionAccess>(`${ticketPath(id)}/discussion-access`, { signal }),
    comment: (id: string, value: CommentDraft, signal: AbortSignal) => client.post<TicketComment, CommentDraft>(`${ticketPath(id)}/comments`,
      { content: value.content, isInternal: value.isInternal }, { signal }),
    comments: async (id: string, search: CommentSearch, signal: AbortSignal) => {
      const data = await client.get<PagedResult<TicketComment>>(`${ticketPath(id)}/comments`, { params: search, signal })
      if (data.items.some(item => item.ticketId !== id || item.isInternal !== search.isInternal)) {
        throw new ApiError('Dữ liệu thảo luận không đúng phạm vi yêu cầu.', 'unexpected')
      }
      return data
    },
    activities: (id: string, page: number, signal: AbortSignal, isInternal?: boolean) => client.get<PagedResult<TicketActivity>>(`${ticketPath(id)}/activities`, {
      params: { page, pageSize: 20, descending: true, isInternal }, signal,
    }),
  }
}
