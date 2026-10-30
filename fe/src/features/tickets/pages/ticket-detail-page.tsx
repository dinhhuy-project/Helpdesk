import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { useCallback, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../shared/api/api-error'
import { useAuth } from '../../auth/hooks/use-auth'
import { LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { formatDateTime } from '../../../shared/lib/date-time'
import { TicketDiscussion } from '../components/ticket-discussion'
import type { CommentCommand } from '../components/ticket-comment-composer'
import { TicketError, TicketUnavailable } from '../components/ticket-state'
import { ticketKeys, useTicketAccess, useTicketDetail } from '../hooks/use-tickets'
import { useAddComment } from '../hooks/use-add-comment'
import type { CommentDraft } from '../types/ticket-discussion'
import { formatTicketNumber, isTicketId } from '../lib/ticket-search'
import { getTicketPriorityLabel, getTicketStatusLabel } from '../types/ticket-enums'
import { useTicketWorkflowActions, type TicketWorkflowActions } from '../hooks/use-ticket-workflow-actions'
import { TicketWorkflowPanel } from '../components/ticket-workflow-panel'
import { useTicketAttachmentUpload, type AttachmentUpload } from '../hooks/use-ticket-attachments'
import { TicketSlaPanel } from '../../sla/components/ticket-sla-panel'
import { useTicketWatch } from '../../notifications/realtime/use-ticket-watch'

export function TicketDetailPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isTicketId(id) ? <TicketAccessBoundary key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} /> : <TicketUnavailable />
}

function TicketAccessBoundary({ id }: { id: string }) {
  const access = useTicketAccess(id)
  useTicketWatch(id, access.data?.canRead === true && !access.isError)
  const { scope } = useAuth()
  const queries = useQueryClient()
  const [params, setParams] = useSearchParams()
  const location = useLocation()
  const [draft, setDraft] = useState<CommentDraft>({ content: '', isInternal: false })
  const [success, setSuccess] = useState('')
  const workflow = useTicketWorkflowActions(id, !!access.data?.canRead && !access.isFetching && !access.isError)
  const refreshResource = useCallback(() => {
    void queries.invalidateQueries({ queryKey: ticketKeys.resource(scope, id) })
  }, [queries, scope, id])
  const upload = useTicketAttachmentUpload(id, refreshResource)
  const command = useAddComment(id, () => {
    setSuccess(draft.isInternal ? 'Đã gửi ghi chú nội bộ.' : 'Đã gửi bình luận công khai.')
    setDraft({ ...draft, content: '' })
    const next = new URLSearchParams(params); next.delete('commentPage'); next.delete('activityPage')
    if (draft.isInternal) next.set('comments', 'internal'); else next.delete('comments')
    setParams(next, { replace: true, state: location.state })
    refreshResource()
  }, refreshResource)
  if (workflow.refreshing) return <><span role="status">{workflow.success || upload.success}</span><LoadingState label="Đang kiểm tra lại yêu cầu và quyền truy cập…" /></>
  if (workflow.readError) return <><span role="status">{workflow.success || upload.success}</span><TicketError error={workflow.readError} onRetry={workflow.reload} /></>
  if (workflow.command.error?.status === 403 || workflow.command.error?.status === 404) return <>{upload.success && <p role="status">{upload.success}</p>}<TicketError error={workflow.command.error} onRetry={workflow.reload} /></>
  if (workflow.workflow.error instanceof ApiError && (workflow.workflow.error.status === 403 || workflow.workflow.error.status === 404)) return <>{upload.success && <p role="status">{upload.success}</p>}<TicketError error={workflow.workflow.error} onRetry={workflow.reload} /></>
  if (access.isFetching || access.isPending) return <>{upload.success && <p role="status">{upload.success}</p>}<PageTitle title="Đang tải yêu cầu" /><LoadingState label="Đang kiểm tra quyền truy cập…" /></>
  if (access.isError) return <>{upload.success && <p role="status">{upload.success}</p>}<TicketError error={access.error} onRetry={() => { void access.refetch() }} /></>
  if (!access.data.canRead) return <>{upload.success && <p role="status">{upload.success}</p>}<TicketUnavailable /></>
  // Unmount detail/activity on access revalidation: old content cannot precede the new decision.
  return <><span role="status" className="comment-success">{workflow.success || success}</span>
    {upload.success && <p role="status" className="comment-success">{upload.success}</p>}
    <TicketContent id={id} draft={draft} onDraftChange={setDraft} command={command} workflow={workflow} upload={upload} refreshResource={refreshResource} /></>
}

function listReturnUrl(state: unknown) {
  if (state && typeof state === 'object' && 'ticketListUrl' in state && typeof state.ticketListUrl === 'string'
    && /^\/tickets(?:\?[^#\\]*)?$/.test(state.ticketListUrl) && !/[\r\n]/.test(state.ticketListUrl)) return state.ticketListUrl
  return '/tickets'
}

function TicketContent({ id, draft, onDraftChange, command, workflow, upload, refreshResource }: {
  id: string; draft: CommentDraft; onDraftChange: (draft: CommentDraft) => void; command: CommentCommand; workflow: TicketWorkflowActions
  upload: AttachmentUpload; refreshResource: () => void
}) {
  const detail = useTicketDetail(id)
  const [discussionError, setDiscussionError] = useState<ApiError | null>(null)
  const { user } = useAuth()
  const location = useLocation()
  if (detail.isPending || detail.isFetching) return <><PageTitle title="Đang tải yêu cầu" /><LoadingState /></>
  if (detail.isError) return <TicketError error={detail.error} onRetry={() => { void detail.refetch() }} />
  // A later resource rejection also hides previously loaded detail. Never infer access from IDs/roles.
  if (discussionError) return <TicketError error={discussionError} onRetry={() => setDiscussionError(null)} />
  const ticket = detail.data
  return <article className="ticket-page"><PageTitle title={`${formatTicketNumber(ticket.ticketNumber)} · ${ticket.title}`} />
    <Link className="ticket-back-link" to={listReturnUrl(location.state)}>← Danh sách yêu cầu</Link>
    <header className="ticket-page-heading"><p className="eyebrow">{formatTicketNumber(ticket.ticketNumber)}</p><h1>{ticket.title}</h1>
      <div className="ticket-labels"><span className="ticket-badge">{getTicketStatusLabel(ticket.status)}</span>
        <span className={`ticket-priority ticket-priority-${ticket.priority}`}>{getTicketPriorityLabel(ticket.priority)}</span></div>
    </header>
    <section className="ticket-card" aria-labelledby="ticket-description"><h2 id="ticket-description">Nội dung yêu cầu</h2>
      <p className="ticket-plain-text">{ticket.description}</p>
      {ticket.resolutionSummary && <><h2>Kết quả giải quyết</h2><p className="ticket-plain-text">{ticket.resolutionSummary}</p></>}
    </section>
    <section className="ticket-card" aria-labelledby="ticket-information"><h2 id="ticket-information">Thông tin yêu cầu</h2>
      <dl className="ticket-information">
        <div><dt>Ngày tạo</dt><dd>{formatDateTime(ticket.createdAt)}</dd></div>
        <div><dt>Cập nhật gần nhất</dt><dd>{formatDateTime(ticket.updatedAt)}</dd></div>
        <div><dt>Ngày giải quyết</dt><dd>{formatDateTime(ticket.resolvedAt)}</dd></div>
        <div><dt>Ngày đóng</dt><dd>{formatDateTime(ticket.closedAt)}</dd></div>
        <div><dt>Người yêu cầu</dt><dd>{ticket.requesterId === user?.id ? 'Bạn' : 'Người dùng khác'}</dd></div>
        <div><dt>Người xử lý</dt><dd>{ticket.assignedAgentId === user?.id ? 'Bạn' : ticket.assignedAgentId ? 'Đã phân công' : 'Chưa phân công'}</dd></div>
        <div><dt>Nhóm hỗ trợ</dt><dd>{ticket.assignedTeamId ? 'Đã phân công nhóm' : 'Chưa phân công nhóm'}</dd></div>
      </dl>
      <details className="ticket-references"><summary>Mã tham chiếu</summary><dl className="ticket-information">
        <div><dt>Yêu cầu</dt><dd>{ticket.id}</dd></div><div><dt>Danh mục</dt><dd>{ticket.categoryId}</dd></div>
        <div><dt>Người yêu cầu</dt><dd>{ticket.requesterId}</dd></div>
        <div><dt>Nhóm hỗ trợ</dt><dd>{ticket.assignedTeamId ?? '—'}</dd></div>
        <div><dt>Người xử lý</dt><dd>{ticket.assignedAgentId ?? '—'}</dd></div>
      </dl></details>
    </section>
    <TicketWorkflowPanel id={id} ticketVersion={ticket.version} actions={workflow} externallyBlocked={command.pending || upload.command.pending} />
    <TicketSlaPanel id={id} onDenied={refreshResource} />
    <TicketDiscussion id={id} draft={draft} onDraftChange={onDraftChange}
      command={{ ...command, disabled: command.disabled || upload.command.pending || workflow.command.pending }}
      onDenied={setDiscussionError} upload={upload} filesBlocked={command.pending || workflow.command.pending} refreshResource={refreshResource} />
  </article>
}
