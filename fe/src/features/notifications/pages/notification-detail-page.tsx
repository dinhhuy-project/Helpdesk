import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import { formatDateTime } from '../../../shared/lib/date-time'
import { LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { Button } from '../../../shared/components/button'
import { notificationKeys, useMarkNotificationRead, useNotificationDetail } from '../hooks/use-notifications'
import { NotificationCommandFeedback, NotificationError } from '../components/notification-state'

export function NotificationDetailPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <NotificationDetail key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} /> : <NotificationError error={new ApiError('Mã thông báo không hợp lệ.', 'http', 404)} />
}
function NotificationDetail({ id }: { id: string }) {
  const { scope } = useAuth()
  const queries = useQueryClient()
  const [rejected, setRejected] = useState<ApiError | null>(null)
  const query = useNotificationDetail(id, !rejected)
  const command = useMarkNotificationRead()
  const [success, setSuccess] = useState(false)
  const location = useLocation()
  const candidate: unknown = location.state?.notificationListUrl
  const back = typeof candidate === 'string' && /^\/notifications(?:\?[^#\\\r\n]*)?$/.test(candidate) ? candidate : '/notifications'
  const denied = command.error?.status === 403 || command.error?.status === 404
  const error = rejected ?? command.error ?? query.error
  useEffect(() => {
    let active = true
    if (error instanceof ApiError && (error.status === 403 || error.status === 404) && !rejected) {
      void queries.cancelQueries({ queryKey: notificationKeys.detail(scope, id) }).then(() => {
        if (!active) return
        setRejected(error)
        queries.removeQueries({ queryKey: notificationKeys.detail(scope, id) })
      })
    }
    return () => { active = false }
  }, [error, rejected, queries, scope, id])
  return <section className="notification-page"><PageTitle title="Chi tiết thông báo" /><h1>Chi tiết thông báo</h1><Link to={back}>Về danh sách thông báo</Link>
    <NotificationCommandFeedback command={command} success={success} />
    {rejected || denied ? <NotificationError error={error} /> : query.isPending || query.isFetching ? <LoadingState label="Đang kiểm tra thông báo hiện tại…" /> : query.isError ?
      <NotificationError error={query.error} onRetry={() => { void command.refresh() }} /> : <article className="notification-item">
        <div><span className="ticket-badge">{query.data.readAt === null ? 'Chưa đọc' : 'Đã đọc'}</span> {query.data.isInternal && <span className="ticket-badge notification-internal">Nội bộ</span>}</div>
        <h2>{query.data.title}</h2>
        <dl className="account-summary"><div><dt>Ngày tạo</dt><dd>{formatDateTime(query.data.createdAt)}</dd></div>
          <div><dt>Đã đọc lúc</dt><dd>{formatDateTime(query.data.readAt)}</dd></div></dl>
        <p><Link className="button button--primary" to={`/tickets/${query.data.ticketId}`}>Mở yêu cầu liên quan</Link></p>
        <p className="field-hint">Ticket được kiểm tra lại quyền khi mở. Thông báo không cấp quyền truy cập Ticket hoặc nội dung nội bộ.</p>
        {query.data.readAt === null && <Button variant="secondary" disabled={command.disabled} onClick={async () => {
          setSuccess(false)
          const result = await command.markRead(id)
          if (result) setSuccess(true)
          else if (command.isCurrent()) await command.refresh()
        }}>Đánh dấu đã đọc</Button>}
      </article>}
  </section>
}
