import { Button } from './button'

export function Pagination({ page, pageSize, totalCount, onPageChange, disabled = false }: {
  page: number; pageSize: number; totalCount: number; onPageChange: (page: number) => void; disabled?: boolean
}) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize))
  return (
    <nav className="pagination" aria-label="Phân trang">
      <span aria-live="polite">Trang {page} / {pages} · {totalCount.toLocaleString('vi-VN')} kết quả</span>
      <div className="pagination-actions">
        <Button variant="secondary" disabled={disabled || page <= 1}
          onClick={() => onPageChange(Math.max(1, Math.min(pages, page - 1)))}>Trang trước</Button>
        <Button variant="secondary" disabled={disabled || page >= pages}
          onClick={() => onPageChange(page + 1)}>Trang sau</Button>
      </div>
    </nav>
  )
}
