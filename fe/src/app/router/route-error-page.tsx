import { Link, isRouteErrorResponse, useRouteError } from 'react-router-dom'
import { Button } from '../../shared/components/button'
import { PageTitle } from '../../shared/components/page-title'
import { ApiError } from '../../shared/api/api-error'
import { ForbiddenPage } from './forbidden-page'

export function RouteErrorPage() {
  const error = useRouteError()
  const status = isRouteErrorResponse(error) || error instanceof ApiError ? error.status : undefined
  if (status === 403) return <ForbiddenPage />
  const notFound = status === 404
  return (
    <main className="error-page">
      <PageTitle title={notFound ? 'Không tìm thấy trang' : 'Không thể mở trang'} />
      <h1>{notFound ? 'Không tìm thấy trang' : 'Không thể mở trang'}</h1>
      <p>{notFound ? 'Vui lòng kiểm tra lại đường dẫn.' : 'Có lỗi xảy ra. Vui lòng tải lại trang để tiếp tục.'}</p>
      {error instanceof ApiError && error.traceId && <p className="field-hint">Mã tra cứu: {error.traceId}</p>}
      <Button onClick={() => window.location.reload()}>Tải lại trang</Button>
      <Link to="/">Về trang chủ</Link>
    </main>
  )
}
