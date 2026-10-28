import { Link, Outlet } from 'react-router-dom'

export function RootLayout() {
  return (
    <>
      <a className="skip-link" href="#main-content">Đến nội dung chính</a>
      <header className="app-header">
        <div className="header-inner">
          <Link to="/" className="brand" aria-label="Helpdesk — Trang chủ">
            <span className="brand-mark" aria-hidden="true">H</span>Helpdesk
          </Link>
          <span className="header-caption">Trung tâm hỗ trợ nội bộ</span>
        </div>
      </header>
      <main id="main-content" className="page-container" tabIndex={-1}><Outlet /></main>
    </>
  )
}
