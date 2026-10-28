import { Link, isRouteErrorResponse, useRouteError } from 'react-router-dom'
import { Button } from '../../shared/components/button'

export function RouteErrorPage() {
  const error = useRouteError()
  const notFound = isRouteErrorResponse(error) && error.status === 404
  return (
    <main className="error-page">
      <h1>{notFound ? 'Không tìm thấy trang' : 'Không thể mở trang'}</h1>
      <p>{notFound ? 'Vui lòng kiểm tra lại đường dẫn.' : 'Có lỗi xảy ra. Vui lòng tải lại trang để tiếp tục.'}</p>
      <Button onClick={() => window.location.reload()}>Tải lại trang</Button>
      <Link to="/">Về trang chủ</Link>
    </main>
  )
}
