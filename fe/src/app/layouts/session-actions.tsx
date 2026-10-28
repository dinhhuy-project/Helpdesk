import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../../features/auth/hooks/use-auth'
import { Button } from '../../shared/components/button'

export function SessionActions() {
  const auth = useAuth()
  const navigate = useNavigate()
  return <nav className="header-actions" aria-label="Tài khoản">
    {auth.status === 'authenticated' ? <>
      <Link to="/account">Tài khoản của tôi</Link>
      <Button variant="secondary" onClick={() => {
        void auth.session.logout()
        navigate('/login', { replace: true })
      }}>Đăng xuất</Button>
    </> : auth.status === 'anonymous' ? <Link to="/login">Đăng nhập</Link> : null}
  </nav>
}
