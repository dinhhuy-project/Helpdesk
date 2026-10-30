import { Link } from 'react-router-dom'
import { useNotificationCount } from '../hooks/use-notifications'

export function NotificationBell() {
  const query = useNotificationCount()
  const count = !query.isFetching && !query.isError ? query.data?.count : undefined
  const label = count === undefined ? 'Thông báo: chưa xác nhận số chưa đọc' : `Thông báo: ${count.toLocaleString('vi-VN')} chưa đọc`
  return <Link className="notification-bell" to="/notifications" aria-label={label}
    title={query.isError ? 'Chưa tải được số chưa đọc. Mở thông báo để tải lại.' : label}>
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
      <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M9 21h6" /></svg>
    <span className="notification-count" aria-hidden="true">{count === undefined ? '—' : count > 99 ? '99+' : count}</span>
  </Link>
}
