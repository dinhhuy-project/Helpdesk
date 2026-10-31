import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Button } from '../../../shared/components/button'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { Permission } from '../../auth/lib/permissions'
import { useCommunityApi, communityReadOptions, publicCommunityKey } from '../hooks/use-community'
import { publicCommunityApi } from '../api/community-api'

export function TicketKnowledgeDraft({ ticketId }: { ticketId: string }) {
  const { api, user } = useCommunityApi()
  const command = useApiCommand()
  const navigate = useNavigate()
  if (!user?.permissions.includes(Permission.KnowledgeManage)) return null
  return <section className="community-card"><h2>Chia sẻ cách giải quyết</h2><p>Tạo bản nháp từ tiêu đề và kết quả xử lý. Nội dung cần được biên soạn và duyệt trước khi công khai.</p>
    <Button disabled={command.disabled} onClick={async () => {
      const result = await command.run(signal => api.fromTicket(ticketId, signal))
      if (result) void navigate(`/admin/knowledge/${result.id}`)
    }}>Soạn bài tri thức từ Ticket</Button>{command.error && <ErrorState error={command.error} />}
    {command.uncertain && <p role="alert">Kiểm tra danh sách bài nháp trước khi tạo lại.</p>}
  </section>
}
export function TicketKnowledgeSuggestions({ title }: { title: string }) {
  const [search, setSearch] = useState('')
  const query = useQuery({ ...communityReadOptions, queryKey: [...publicCommunityKey, 'suggestions', search], enabled: search.length >= 3,
    queryFn: ({ signal }) => publicCommunityApi.list('knowledge', { page: 1, search }, signal) })
  return <aside className="community-card"><h2>Thử tự giải quyết trước</h2><p>Tra cứu hướng dẫn theo tiêu đề bạn đã nhập hoặc kiểm tra xem có sự cố chung đang diễn ra.</p>
    <div className="community-links"><Button variant="secondary" disabled={title.trim().length < 3} onClick={() => setSearch(title.trim().slice(0, 200))}>Tìm hướng dẫn liên quan</Button><Link to="/incidents">Kiểm tra sự cố chung</Link></div>
    {search && (query.isPending ? <LoadingState /> : query.error ? <ErrorState error={query.error} /> : !query.data.items.length ? <EmptyState title="Chưa có hướng dẫn phù hợp với từ khóa" /> :
      <ul>{query.data.items.slice(0, 3).map(row => <li key={row.id}><Link to={`/knowledge/${row.id}`}>{row.title}</Link></li>)}</ul>)}
    <p><Link to="/knowledge">Tra cứu bằng từ khóa khác</Link></p>
  </aside>
}
