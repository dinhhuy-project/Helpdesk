import { Link } from 'react-router-dom'
import { PageTitle } from '../../shared/components/page-title'
import { useAuth } from '../../features/auth/hooks/use-auth'

export function ForbiddenPage() {
  const { status } = useAuth()
  return (
    <section className="error-page" role="alert">
      <PageTitle title="Không có quyền truy cập" />
      <p className="eyebrow">403</p><h1>Không có quyền truy cập</h1>
      <p>Tài khoản hiện tại không có quyền mở nội dung này. Liên hệ quản trị viên nếu bạn cần được cấp quyền.</p>
      <Link to={status === 'authenticated' ? '/account' : '/login'}>
        {status === 'authenticated' ? 'Về tài khoản' : 'Đăng nhập'}
      </Link>
    </section>
  )
}
