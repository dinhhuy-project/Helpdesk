import { useEffect, useState } from 'react'
import { useLocation, useSearchParams } from 'react-router-dom'
import type { ApiError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useAuth } from '../../auth/hooks/use-auth'
import { useTicketActivities, useTicketComments, useTicketDiscussionAccess } from '../hooks/use-tickets'
import { parseCommentPage, parseCommentVisibility } from '../lib/comment-search'
import { isTicketAccessError } from '../lib/ticket-errors'
import { parseActivityPage } from '../lib/ticket-search'
import type { CommentDraft, TicketDiscussionAccess } from '../types/ticket-discussion'
import { TicketActivityList } from './ticket-activity-list'
import { TicketCommentComposer } from './ticket-comment-composer'
import type { CommentCommand } from './ticket-comment-composer'
import { Dialog } from '../../../shared/components/dialog'
import { TicketAttachments } from './ticket-attachments'
import type { AttachmentUpload } from '../hooks/use-ticket-attachments'
import type { TicketComment } from '../types/ticket-discussion'

interface DiscussionProps {
  id: string; draft: CommentDraft; onDraftChange: (value: CommentDraft) => void; command: CommentCommand; onDenied: (error: ApiError) => void
  upload: AttachmentUpload; filesBlocked: boolean; refreshResource: () => void
}
export function TicketDiscussion(props: DiscussionProps) {
  const { onDenied } = props
  const access = useTicketDiscussionAccess(props.id)
  const denial = isTicketAccessError(access.error) ? access.error : null
  useEffect(() => { if (denial) onDenied(denial) }, [denial, onDenied])
  if (denial) return null
  // Unmount private query observers during capability revalidation, so gcTime=0 purges them.
  if (access.isPending || access.isFetching) return <LoadingState label="Đang kiểm tra quyền thảo luận…" />
  if (access.isError) return <ErrorState error={access.error} onRetry={() => { void access.refetch() }} />
  const value = access.data
  return <VerifiedDiscussion key={`${value.canReadInternal}:${value.canCommentPublic}:${value.canCommentInternal}`} {...props}
    access={value} refresh={() => { void access.refetch() }} />
}
function VerifiedDiscussion({ id, draft, onDraftChange, command, onDenied, access, refresh, upload, filesBlocked, refreshResource }: DiscussionProps & {
  access: TicketDiscussionAccess; refresh: () => void
}) {
  const { user } = useAuth()
  const [params, setParams] = useSearchParams()
  const location = useLocation()
  const [fileComment, setFileComment] = useState<TicketComment | null>(null)
  const internal = access.canReadInternal === true
  const visibility = parseCommentVisibility(params)
  const page = parseCommentPage(params)
  const activityPage = parseActivityPage(params)
  const commentsAllowed = visibility !== null && page !== null && (visibility === 'public' || internal)
  const comments = useTicketComments(id, { page: page ?? 1, pageSize: 20, descending: true, isInternal: visibility === 'internal' && internal }, commentsAllowed)
  const activity = useTicketActivities(id, activityPage ?? 1, activityPage !== null, internal)
  const denial = [comments.error, activity.error].find(isTicketAccessError)
  useEffect(() => { if (denial) onDenied(denial) }, [denial, onDenied])
  useEffect(() => {
    if (!internal && draft.isInternal) onDraftChange({ content: '', isInternal: false })
  }, [internal, draft.isInternal, onDraftChange])

  function view(isInternal: boolean) {
    const next = new URLSearchParams(params)
    next.delete('commentPage')
    if (isInternal) next.set('comments', 'internal'); else next.delete('comments')
    setParams(next, { state: location.state })
  }
  if (denial) return null
  return <>
    <div className="ticket-card"><TicketAttachments id={id} upload={upload} blocked={filesBlocked} onDenied={refreshResource} /></div>
    <section className="ticket-card" aria-labelledby="ticket-comments"><div className="comment-section-heading"><h2 id="ticket-comments">Thảo luận</h2>
      <Button variant="secondary" onClick={refresh}>Tải lại thảo luận</Button></div>
      <div className="comment-modes" role="group" aria-label="Loại bình luận hiển thị">
        <Button variant="secondary" aria-pressed={visibility === 'public'} onClick={() => view(false)}>Công khai</Button>
        {internal && <Button variant="secondary" aria-pressed={visibility === 'internal'} onClick={() => view(true)}>Nội bộ</Button>}
      </div>
      {!commentsAllowed ? <div role="alert"><p>{visibility === 'internal' && !internal ? 'Bạn không có quyền đọc ghi chú nội bộ của yêu cầu này.' : 'Bộ lọc bình luận không hợp lệ.'}</p>
        <Button variant="secondary" onClick={() => view(false)}>Xem bình luận công khai</Button></div>
        : <div role="region" aria-label={visibility === 'internal' ? 'Ghi chú nội bộ' : 'Bình luận công khai'} aria-busy={comments.isFetching}>
          {comments.isPending || comments.isFetching ? <LoadingState label="Đang tải bình luận…" />
            : comments.isError ? <ErrorState error={comments.error} onRetry={() => { void comments.refetch() }} />
              : comments.data && <>
                {comments.data.items.length ? <ol className="ticket-comments">{comments.data.items.map(comment => <li key={comment.id} className={comment.isInternal ? 'comment-item is-internal' : 'comment-item'}>
                  <div className="comment-item-heading"><strong>{comment.authorId === user?.id ? 'Bạn' : 'Người dùng'}</strong>
                    <span className="comment-visibility">{comment.isInternal ? 'Nội bộ' : 'Công khai'}</span>
                    <time dateTime={comment.createdAt}>{formatDateTime(comment.createdAt)}</time></div>
                  <p className="ticket-plain-text">{comment.content}</p>
                  <Button variant="secondary" disabled={upload.command.pending} onClick={() => setFileComment(comment)}>File của bình luận</Button>
                </li>)}</ol> : <EmptyState title={visibility === 'internal' ? 'Chưa có ghi chú nội bộ' : 'Chưa có bình luận công khai'} description="Không có nội dung tại trang hiện tại." />}
                <Pagination page={comments.data.page} pageSize={comments.data.pageSize} totalCount={comments.data.totalCount} onPageChange={value => {
                  const next = new URLSearchParams(params); if (value === 1) next.delete('commentPage'); else next.set('commentPage', String(value)); setParams(next, { state: location.state })
                }} />
              </>}
        </div>}
      <TicketCommentComposer access={access} draft={draft} onDraftChange={onDraftChange} command={command} />
      <p className="field-hint">Để gắn file với bình luận, gửi nội dung trước rồi mở “File của bình luận” tại bình luận đã tạo.</p>
    </section>
    <Dialog open={fileComment !== null} onOpenChange={open => { if (!open && !upload.command.pending) setFileComment(null) }} title="File đính kèm bình luận">
      {fileComment && <TicketAttachments key={fileComment.id} id={id} comment={fileComment} upload={upload} blocked={filesBlocked} onDenied={refreshResource} />}
    </Dialog>
    <section className="ticket-card" aria-labelledby="ticket-activity"><h2 id="ticket-activity">Lịch sử hoạt động</h2>
      {activityPage === null ? <div role="alert"><p>Trang hoạt động không hợp lệ.</p><Button variant="secondary" onClick={() => {
        const next = new URLSearchParams(params); next.delete('activityPage'); setParams(next, { state: location.state })
      }}>Về trang hoạt động đầu</Button></div>
        : activity.isPending || activity.isFetching ? <LoadingState label="Đang tải hoạt động…" />
          : activity.isError ? <ErrorState error={activity.error} onRetry={() => { void activity.refetch() }} />
            : activity.data && <TicketActivityList data={activity.data} userId={user?.id} onPageChange={value => {
              const next = new URLSearchParams(params); if (value === 1) next.delete('activityPage'); else next.set('activityPage', String(value)); setParams(next, { state: location.state })
            }} />}
    </section>
  </>
}
