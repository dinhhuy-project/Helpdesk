import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <section className="error-page">
      <p className="eyebrow">404</p><h1>Không tìm thấy trang</h1>
      <p>Đường dẫn không tồn tại. Bạn có thể quay về trang chủ để tiếp tục.</p>
      <Link to="/">Về trang chủ</Link>
    </section>
  )
}
