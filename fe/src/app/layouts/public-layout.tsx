import type { ReactNode } from 'react'
import { Brand } from './brand'
import { SessionActions } from './session-actions'
import { Link } from 'react-router-dom'

export function PublicLayout({ children }: { children: ReactNode }) {
  return <>
    <a className="skip-link" href="#main-content">Đến nội dung chính</a>
    <header className="app-header"><div className="header-inner"><Brand /><nav className="community-links" aria-label="Tra cứu công khai"><Link to="/knowledge">Kho tri thức</Link><Link to="/incidents">Sự cố chung</Link></nav><SessionActions /></div></header>
    <main id="main-content" className="page-container" tabIndex={-1}>{children}</main>
  </>
}
