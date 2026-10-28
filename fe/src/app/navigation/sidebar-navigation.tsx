import { NavLink } from 'react-router-dom'
import { useAuth } from '../../features/auth/hooks/use-auth'
import { visibleNavigation } from './navigation'

export function SidebarNavigation({ onNavigate }: { onNavigate?: () => void }) {
  const auth = useAuth()
  const verified = auth.status === 'authenticated' && !!auth.user && !auth.profilePending && !auth.profileError
  const items = visibleNavigation(auth.user?.permissions ?? [], verified)
  const groups = [...new Set(items.map(item => item.group))]
  return (
    <nav className="sidebar-navigation" aria-label="Điều hướng chính">
      {!verified && <p className="sidebar-status" role="status">
        {auth.profileError ? 'Chưa xác nhận được quyền truy cập.' : 'Đang xác nhận quyền truy cập…'}
      </p>}
      {groups.map(group => <section className="navigation-group" key={group} aria-label={group}>
        <h2>{group}</h2>
        <ul>{items.filter(item => item.group === group).map(item => <li key={item.id}>
          {item.available ? <NavLink to={item.path} end className={({ isActive }) => `navigation-item${isActive ? ' is-active' : ''}`}
            onClick={onNavigate}>{item.label}</NavLink>
            : <span className="navigation-item is-unavailable" aria-disabled="true">
              <span>{item.label}</span><span className="soon-badge">Sắp có</span>
            </span>}
        </li>)}</ul>
      </section>)}
    </nav>
  )
}
