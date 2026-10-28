import { Link } from 'react-router-dom'

export function Brand() {
  return <Link to="/" className="brand" aria-label="Helpdesk — Trang chủ">
    <span className="brand-mark" aria-hidden="true">H</span>Helpdesk
  </Link>
}
