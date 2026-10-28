import { useAuth } from '../hooks/use-auth'
import type { SessionEndReason } from '../types/auth'

const notices: Record<SessionEndReason, string> = {
  signedOut: 'Bạn đã đăng xuất.',
  expired: 'Phiên đã hết hạn. Vui lòng đăng nhập lại.',
  revoked: 'Phiên không còn hiệu lực hoặc tài khoản đã bị vô hiệu hóa. Vui lòng đăng nhập lại.',
  refreshFailed: 'Không thể gia hạn phiên an toàn. Vui lòng đăng nhập lại.',
  passwordChanged: 'Đổi mật khẩu thành công. Vui lòng đăng nhập lại bằng mật khẩu mới.',
  passwordUncertain: 'Chưa xác nhận được kết quả đổi mật khẩu. Vui lòng đăng nhập lại; dùng mật khẩu mới nếu thay đổi đã được lưu.',
  logoutFailed: 'Đã xóa phiên trên thiết bị này nhưng chưa xác nhận thu hồi trên máy chủ. Phiên cũ có thể còn hiệu lực đến khi hết hạn.',
}

export function SessionNotice() {
  const { reason, logoutError } = useAuth()
  if (!reason) return null
  return (
    <div className="auth-message" role={reason === 'logoutFailed' ? 'alert' : 'status'}>
      <p>{notices[reason]}</p>
      {reason === 'logoutFailed' && logoutError?.retryAfterSeconds !== undefined
        && <p>Máy chủ giới hạn thao tác. Vui lòng chờ {logoutError.retryAfterSeconds} giây trước khi đăng nhập.</p>}
    </div>
  )
}
