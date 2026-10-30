import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { PageTitle } from '../../../shared/components/page-title'
import { Button } from '../../../shared/components/button'
import { EmptyState, LoadingState } from '../../../shared/components/async-state'
import { FormField, Select } from '../../../shared/components/form-field'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useNotificationList, useMarkNotificationRead } from '../hooks/use-notifications'
import { notificationSearchUrl, parseNotificationSearch } from '../lib/notification-search'
import { NotificationCommandFeedback, NotificationError } from '../components/notification-state'
import type { NotificationSearch } from '../types/notification'
import { RealtimeStatus } from '../components/realtime-status'

export function NotificationListPage() {
  const [params, setParams] = useSearchParams()
  const parsed = parseNotificationSearch(params)
  const search = parsed.value ?? { page: 1, pageSize: 20 }
  const query = useNotificationList(search, parsed.value !== undefined)
  const command = useMarkNotificationRead()
  const [success, setSuccess] = useState(false)
  const change = (value: NotificationSearch) => setParams(notificationSearchUrl(value))
  return <section className="notification-page"><PageTitle title="Thông báo" /><p className="eyebrow">Cá nhân</p><h1>Thông báo</h1>
    <p>Những cập nhật dành cho bạn. Khi quyền truy cập thay đổi, một số thông báo có thể không còn khả dụng.</p>
    <RealtimeStatus />
    <div className="notification-filters">
      <FormField label="Trạng thái đọc">{field => <Select {...field} value={search.unread === undefined ? '' : String(search.unread)}
        onChange={event => change({ ...search, page: 1, unread: event.target.value === '' ? undefined : event.target.value === 'true' })}>
        <option value="">Tất cả</option><option value="true">Chưa đọc</option><option value="false">Đã đọc</option></Select>}</FormField>
      <FormField label="Thông báo mỗi trang">{field => <Select {...field} value={search.pageSize} onChange={event => change({ ...search, page: 1, pageSize: Number(event.target.value) })}>
        {![10, 20, 50, 100].includes(search.pageSize) && <option value={search.pageSize}>{search.pageSize}</option>}
        {[10, 20, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}</Select>}</FormField>
      <Button variant="secondary" disabled={query.isFetching || command.pending || command.remaining > 0} onClick={() => { void command.refresh() }}>Tải lại thông báo</Button>
    </div>
    <NotificationCommandFeedback command={command} success={success} />
    {parsed.error ? <div role="alert"><p>{parsed.error}</p><Button onClick={() => setParams({})}>Xóa bộ lọc</Button></div> :
      query.isPending || query.isFetching ? <LoadingState label="Đang kiểm tra thông báo hiện tại…" /> : query.isError ? <NotificationError error={query.error} onRetry={() => { void command.refresh() }} /> : <>
        {query.data.items.length ? <ul className="notification-list" aria-label="Danh sách thông báo">{query.data.items.map(item => <li key={item.id} className={`notification-item${item.readAt === null ? ' notification-item--unread' : ''}`}>
          <div><span className="ticket-badge">{item.readAt === null ? 'Chưa đọc' : 'Đã đọc'}</span> {item.isInternal && <span className="ticket-badge notification-internal">Nội bộ</span>}</div>
          <h2><Link to={`/notifications/${item.id}`} state={{ notificationListUrl: `/notifications${params.size ? `?${params}` : ''}` }}>{item.title}</Link></h2>
          <p className="field-hint">Tạo lúc {formatDateTime(item.createdAt)}{item.readAt && ` · Đã đọc lúc ${formatDateTime(item.readAt)}`}</p>
          {item.readAt === null && <Button variant="secondary" disabled={command.disabled} onClick={async () => {
            setSuccess(false)
            const result = await command.markRead(item.id)
            if (result) setSuccess(true)
            else if (command.isCurrent()) await command.refresh()
          }} aria-label={`Đánh dấu đã đọc: ${item.title}`}>Đánh dấu đã đọc</Button>}
        </li>)}</ul> : <EmptyState title="Không có thông báo trong trang này" description="Thử bộ lọc khác hoặc tải lại khi có cập nhật mới dành cho bạn." />}
        <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={page => change({ ...search, page })} disabled={command.pending} />
      </>}
    <p className="field-hint">Thông báo được cập nhật khi mở, quay lại tab hoặc mỗi phút ở tab đang hoạt động. Có thể tải lại thủ công khi kết nối trở lại.</p>
  </section>
}
