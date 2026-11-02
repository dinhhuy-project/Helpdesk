import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Button } from '../../../shared/components/button'
import { FormField, Input } from '../../../shared/components/form-field'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { formatDateTime } from '../../../shared/lib/date-time'
import { publicCommunityApi } from '../api/community-api'
import { communityReadOptions, publicCommunityKey } from '../hooks/use-community'

export function KnowledgeRetrieval() {
  const [params, setParams] = useSearchParams()
  const question = (params.get('question') ?? '').trim()
  const [input, setInput] = useState(question)
  const valid = question.length >= 2 && question.length <= 200 && !Array.from(question).some(char => {
    const code = char.charCodeAt(0)
    return code <= 31 || (code >= 127 && code <= 159)
  })
  const query = useQuery({ ...communityReadOptions, queryKey: [...publicCommunityKey, 'retrieval', question],
    queryFn: ({ signal }) => publicCommunityApi.retrieve(question, signal), enabled: valid, refetchInterval: 30000 })
  return <section className="community-retrieval" aria-labelledby="retrieval-heading">
    <h2 id="retrieval-heading">Tìm đúng đoạn hướng dẫn</h2>
    <p>Tra cứu trong hướng dẫn thực tế đã duyệt, có hỗ trợ tiếng Việt không dấu và từ viết tắt phổ biến. Kết quả là trích đoạn từ tài liệu, cần đọc phạm vi áp dụng trước khi thực hiện.</p>
    <form className="community-filters" role="search" aria-label="Tìm đoạn hướng dẫn" onSubmit={event => {
      event.preventDefault(); const next = new URLSearchParams(params); next.set('question', input.trim()); setParams(next)
    }}>
      <FormField label="Bạn đang cần hỗ trợ gì?">{props => <Input {...props} required minLength={2} maxLength={200} value={input}
        onChange={event => setInput(event.target.value)} placeholder="Ví dụ: làm sao thay đổi mật khẩu, tai file len Ticket…" />}</FormField>
      <Button type="submit" disabled={input.trim().length < 2}>Tìm đoạn hướng dẫn</Button>
    </form>
    {question && !valid && <p role="alert">Nhập câu hỏi từ 2 đến 200 ký tự, không có ký tự điều khiển.</p>}
    {valid && (query.isPending ? <LoadingState /> : query.error ? <ErrorState error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      <p role="status">{query.data.items.length} đoạn hướng dẫn phù hợp</p>
      {!query.data.items.length && <EmptyState title="Chưa tìm thấy đoạn hướng dẫn phù hợp" description="Thử nêu rõ thao tác hoặc tên ứng dụng. Kho tri thức cần có bài thực tế được duyệt; dữ liệu minh họa và bản nháp không được dùng cho truy xuất này." />}
      {query.data.candidateLimitReached && <p className="field-hint">Có nhiều tài liệu phù hợp. Hãy thêm tên ứng dụng hoặc thao tác để thu hẹp câu hỏi.</p>}
      <div className="community-grid">{query.data.items.map((row, index) => <article className="community-card" key={`${row.entryId}:${row.section}:${row.step}:${index}`}>
        <p className="eyebrow">{row.section}{row.step !== null ? ` · Bước ${row.step}` : ''}</p>
        <h3><Link to={row.sourceUrl}>{row.title}</Link></h3>
        <blockquote className="community-content">{row.text}</blockquote>
        <p><strong>Phạm vi:</strong> {row.applicability}</p>
        <p className="field-hint">Nguồn: {row.sourceName} · Phiên bản {row.revision} · Xuất bản {formatDateTime(row.publishedAt)}</p>
        <p className="field-hint">Ngày cập nhật tại nguồn: {row.sourceUpdatedOn ? row.sourceUpdatedOn.split('-').reverse().join('/') : 'Chưa xác định'}</p>
      </article>)}</div>
    </>)}
  </section>
}
