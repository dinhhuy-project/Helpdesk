export interface TicketDiscussionAccess { canReadInternal: boolean; canCommentPublic: boolean; canCommentInternal: boolean }
export interface TicketComment {
  id: string
  ticketId: string
  authorId: string
  content: string
  isInternal: boolean
  createdAt: string
  updatedAt: string | null
}
export interface CommentDraft { content: string; isInternal: boolean }
export interface CommentSearch { page: number; pageSize: number; descending: boolean; isInternal: boolean }
