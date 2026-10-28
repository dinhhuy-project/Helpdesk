import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { CurrentProfileBoundary } from '../../features/auth/components/current-profile-boundary'
import { useAuth } from '../../features/auth/hooks/use-auth'
import { roleLabel } from '../../features/auth/lib/identity-labels'
import { Button } from '../../shared/components/button'
import { Dialog } from '../../shared/components/dialog'
import { SidebarNavigation } from '../navigation/sidebar-navigation'
import { Brand } from './brand'
import { SessionActions } from './session-actions'

export function ApplicationLayout({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const location = useLocation()
  const [openAtLocation, setOpenAtLocation] = useState<string | null>(null)
  const menuOpen = openAtLocation === location.key
  const setMenuOpen = (open: boolean) => setOpenAtLocation(open ? location.key : null)
  const verified = !auth.profilePending && !auth.profileError

  useEffect(() => {
    const media = window.matchMedia('(min-width: 900px)')
    const closeWideMenu = () => { if (media.matches) setOpenAtLocation(null) }
    media.addEventListener('change', closeWideMenu)
    return () => media.removeEventListener('change', closeWideMenu)
  }, [])

  useEffect(() => {
    if (!menuOpen) return
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.body.style.overflow = previous }
  }, [menuOpen])

  return (
    <div className="application-frame">
      <a className="skip-link" href="#main-content">Đến nội dung chính</a>
      <aside className="desktop-sidebar"><Brand /><SidebarNavigation /></aside>
      <div className="workspace">
        <header className="workspace-header">
          <Button className="mobile-menu-button" variant="secondary" aria-label="Mở menu điều hướng"
            aria-expanded={menuOpen} aria-controls="mobile-navigation" onClick={() => setMenuOpen(true)}>☰</Button>
          <div className="workspace-identity">
            <p className="workspace-greeting">{verified && auth.user ? `Xin chào, ${auth.user.displayName}` : 'Đang xác nhận tài khoản…'}</p>
            {verified && auth.user && <p className="field-hint workspace-roles">
              {auth.user.roles.length ? auth.user.roles.map(roleLabel).join(' · ') : 'Chưa được gán vai trò'}
            </p>}
          </div>
          <SessionActions />
        </header>
        <main id="main-content" className="workspace-content" tabIndex={-1}>
          <CurrentProfileBoundary>{children}</CurrentProfileBoundary>
        </main>
      </div>
      <Dialog open={menuOpen} onOpenChange={setMenuOpen} title="Điều hướng" className="navigation-dialog">
        <div id="mobile-navigation"><SidebarNavigation onNavigate={() => setMenuOpen(false)} /></div>
      </Dialog>
    </div>
  )
}
