import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { Button } from '../../../shared/components/button'
import { FormField, Input } from '../../../shared/components/form-field'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { publicCommunityApi } from '../api/community-api'
import { communityReadOptions, useCommunityApi } from '../hooks/use-community'

export function WikipediaReferences() {
  const [open, setOpen] = useState(false)
  return <section className="community-card knowledge-source-panel" aria-label="Tài liệu tham khảo Wikipedia">
    <h2>Tham khảo Wikipedia tiếng Việt</h2><p>Tìm hiểu khái niệm và kiến thức phổ thông. Đây là nguồn bên ngoài; hướng dẫn đã duyệt của đơn vị nằm ở phần kho tri thức bên trên.</p>
    <Button variant="secondary" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? 'Đóng tra cứu Wikipedia' : 'Mở tra cứu Wikipedia'}</Button>
    {open && <WikipediaSearch />}
  </section>
}

function WikipediaSearch() {
  const [input, setInput] = useState('')
  const [request, setRequest] = useState({ search: '', offset: 0 })
  const query = useQuery({ ...communityReadOptions, staleTime: 300000, enabled: request.search.length > 0,
    queryKey: ['wikipedia-references', request], queryFn: ({ signal }) => publicCommunityApi.wikipedia(request.search, request.offset, signal) })
  return <div><form className="community-filters" onSubmit={event => { event.preventDefault(); if (input.trim()) setRequest({ search: input.trim(), offset: 0 }) }}>
    <FormField label="Từ khóa Wikipedia" hint="Chỉ từ khóa bạn gửi tại đây được chuyển tới Wikipedia. Không nhập thông tin cá nhân hoặc nội dung Ticket nội bộ.">{props => <Input {...props} type="search" required maxLength={150} value={input} onChange={event => setInput(event.target.value)} />}</FormField>
    <Button type="submit" disabled={!input.trim() || query.isFetching}>Tìm trên Wikipedia</Button>
  </form>
    {request.search && (query.isPending ? <LoadingState /> : query.error ? <ErrorState error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      {!query.data.items.length ? <EmptyState title="Chưa tìm thấy tài liệu tham khảo" /> : <ul className="knowledge-reference-list">{query.data.items.map(row => <li key={row.url}>
        <a href={row.url} target="_blank" rel="noopener noreferrer">{row.title}<span className="sr-only"> (Wikipedia, mở tab mới)</span></a>
        {row.updatedAt && <p className="field-hint">Cập nhật trên Wikipedia: {formatDateTime(row.updatedAt)}</p>}
      </li>)}</ul>}
      <div className="community-links"><Button variant="secondary" disabled={query.isFetching || request.offset === 0} onClick={() => setRequest({ ...request, offset: Math.max(0, request.offset - 5) })}>Wikipedia: trang trước</Button>
        <Button variant="secondary" disabled={query.isFetching || query.data.nextOffset === null} onClick={() => { if (query.data.nextOffset !== null) setRequest({ ...request, offset: query.data.nextOffset }) }}>Wikipedia: trang sau</Button></div>
    </>)}
    <p className="field-hint">Liên kết mở bài gốc trên Wikipedia, nơi hiển thị tác giả/lịch sử và điều kiện sử dụng. Kết quả này không tự trở thành bài Helpdesk được duyệt.</p>
  </div>
}

export function BookStackImport() {
  const [open, setOpen] = useState(false)
  return <section className="community-card knowledge-source-panel"><h2>Nhập tài liệu từ wiki</h2>
    <p>Chọn trang trong sách BookStack đã được cấu hình. Bài nhập là bản nháp, cần biên tập và duyệt trước khi công khai.</p>
    <Button variant="secondary" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? 'Đóng nguồn BookStack' : 'Nhập từ BookStack'}</Button>
    {open && <BookStackBrowser />}
  </section>
}

function BookStackBrowser() {
  const { api, scope } = useCommunityApi()
  const navigate = useNavigate()
  const queries = useQueryClient()
  const [input, setInput] = useState('')
  const [request, setRequest] = useState({ search: '', page: 1 })
  const [selected, setSelected] = useState<number | null>(null)
  const [reviewed, setReviewed] = useState(false)
  const command = useApiCommand()
  const list = useQuery({ ...communityReadOptions, queryKey: ['community-source', scope, 'bookstack', request], queryFn: ({ signal }) => api.bookStackPages(request.search, request.page, signal) })
  const preview = useQuery({ ...communityReadOptions, enabled: selected !== null, queryKey: ['community-source', scope, 'bookstack-preview', selected], queryFn: ({ signal }) => api.bookStackPreview(selected ?? 0, signal) })
  return <div><form className="community-filters" onSubmit={event => { event.preventDefault(); setRequest({ search: input.trim(), page: 1 }); setSelected(null); setReviewed(false) }}>
    <FormField label="Tìm trang BookStack">{props => <Input {...props} type="search" maxLength={150} value={input} onChange={event => setInput(event.target.value)} />}</FormField><Button type="submit">Tìm trong wiki</Button>
  </form>
    {list.isPending ? <LoadingState /> : list.error ? <><ErrorState error={list.error} onRetry={() => { void list.refetch() }} /><p className="field-hint">Nếu nguồn chưa được bật, quản trị viên cần cấu hình URL, token đọc và ID sách BookStack cho backend.</p></> : <>
      {!list.data.items.length && <EmptyState title="Chưa có trang trong sách được phép nhập" />}
      <ul className="knowledge-reference-list">{list.data.items.map(row => <li key={row.id}><Button variant="secondary" disabled={command.pending} onClick={() => { setSelected(row.id); setReviewed(false) }}>{row.title} · Xem trước</Button></li>)}</ul>
      <Pagination page={request.page} pageSize={12} totalCount={list.data.totalCount} onPageChange={page => { setRequest({ ...request, page }); setSelected(null); setReviewed(false) }} />
    </>}
    {selected !== null && <section className="knowledge-source-preview" aria-label="Xem trước trang wiki">
      {preview.isPending ? <LoadingState /> : preview.error ? <ErrorState error={preview.error} onRetry={() => { void preview.refetch() }} /> : <>
        <h3>{preview.data.title}</h3><p className="field-hint">Nguồn: BookStack · {preview.data.revision}</p>
        <div className="community-content">{preview.data.content}</div><p>Ghi công: {preview.data.attribution}</p><p>Quyền sử dụng: {preview.data.license}</p>
        <label className="community-review"><input type="checkbox" checked={reviewed} disabled={command.disabled} onChange={event => setReviewed(event.target.checked)} />Tôi hiểu bài nhập chỉ là bản nháp và phải kiểm tra lại trước khi xuất bản.</label>
        <Button disabled={!reviewed || command.disabled || preview.isFetching} onClick={async () => {
          const result = await command.run(signal => api.importBookStack(selected, signal))
          if (result) { await queries.invalidateQueries({ queryKey: ['community-admin'] }); void navigate(`/admin/knowledge/${result.entry.id}`) }
        }}>Nhập / mở bản nháp đã nhập</Button>
      </>}
    </section>}
    {command.error && <ErrorState error={command.error} />}
    {command.uncertain && <p role="alert">Chưa xác định được kết quả. Kiểm tra danh sách trước khi gửi lại; cùng trang BookStack không tạo bản sao hay ghi đè bài đã nhập.</p>}
  </div>
}
