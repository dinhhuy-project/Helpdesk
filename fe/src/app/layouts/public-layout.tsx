import type { ReactNode } from 'react'
import { Brand } from './brand'
import { SessionActions } from './session-actions'

export function PublicLayout({ children }: { children: ReactNode }) {
  return <>
    <a className="skip-link" href="#main-content">Đến nội dung chính</a>
    <header className="app-header"><div className="header-inner"><Brand /><SessionActions /></div></header>
    <main id="main-content" className="page-container" tabIndex={-1}>{children}</main>
  </>
}
