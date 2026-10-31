import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '../../../shared/components/button'
import { FormField, Input, Select } from '../../../shared/components/form-field'
import { ErrorState, EmptyState, LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { isGuid } from '../../../shared/lib/identifier'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { useAuth } from '../../auth/hooks/use-auth'
import { communityLabels, incidentLabels, publicCommunityApi, type CommunityKind, type PublicEntry } from '../api/community-api'
import { communityReadOptions, publicCommunityKey, useCommunityApi, usePublicEntries, usePublicEntry } from '../hooks/use-community'
import { WikipediaReferences } from '../components/knowledge-sources'

export function CommunityPublicList({ kind }: { kind: CommunityKind }) {
  const [params, setParams] = useSearchParams()
  const rawPage = Number(params.get('page') || 1)
  const page = Number.isSafeInteger(rawPage) && rawPage > 0 && rawPage <= 100000 ? rawPage : 1
  const search = { page, search: params.get('search') ?? '', topic: params.get('topic') ?? '', active: params.get('active') ?? (kind === 'incidents' ? 'true' : '') }
  const query = usePublicEntries(kind, search)
  return <section className="community-page"><PageTitle title={communityLabels[kind]} />
    <header className="community-heading"><p className="eyebrow">Cộng đồng Helpdesk · Tra cứu mở</p><h1>{communityLabels[kind]}</h1>
      <p>{kind === 'knowledge' ? 'Tìm hướng dẫn đã được duyệt và tự giải quyết trước khi gửi yêu cầu hỗ trợ.' : 'Theo dõi sự cố đang ảnh hưởng đến cộng đồng và các cập nhật từ nhóm hỗ trợ.'}</p>
      <nav className="community-links" aria-label="Tra cứu cộng đồng"><Link to="/knowledge">Kho tri thức</Link><Link to="/incidents">Sự cố chung</Link><Link to="/login">Đăng nhập để gửi yêu cầu</Link></nav></header>
    <PublicSearch key={`${kind}:${params}`} kind={kind} initial={search} onApply={next => setParams(next)} />
    {query.isPending ? <LoadingState /> : query.error ? <ErrorState error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      <p className="field-hint" role="status">{query.data.totalCount} kết quả · Tự cập nhật khi có thay đổi.</p>
      {!query.data.items.length ? <EmptyState title="Chưa có nội dung phù hợp" description="Thử từ khóa khác hoặc xem lại sau. Chỉ nội dung đã xuất bản được hiển thị tại đây." /> :
        <div className="community-grid">{query.data.items.map(row => <EntryCard key={row.id} row={row} kind={kind} />)}</div>}
      <Pagination page={page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={next => { const p = new URLSearchParams(params); p.set('page', String(next)); setParams(p) }} />
    </>}
    {kind === 'knowledge' && <WikipediaReferences />}
  </section>
}
function PublicSearch({ kind, initial, onApply }: { kind: CommunityKind; initial: { search: string; topic: string; active: string }; onApply: (params: URLSearchParams) => void }) {
  const [search, setSearch] = useState(initial.search)
  const [topic, setTopic] = useState(initial.topic)
  const [active, setActive] = useState(initial.active)
  return <form className="community-filters" role="search" aria-label="Tra cứu nội dung công khai" onSubmit={event => {
    event.preventDefault(); const p = new URLSearchParams(); if (search.trim()) p.set('search', search.trim()); if (topic.trim()) p.set('topic', topic.trim()); if (kind === 'incidents') p.set('active', active); onApply(p)
  }}>
    <FormField label="Tìm kiếm hướng dẫn hoặc sự cố">{props => <Input {...props} type="search" value={search} maxLength={200} onChange={event => setSearch(event.target.value)} placeholder="Ví dụ: VPN, email, máy in…" />}</FormField>
    <FormField label="Chủ đề">{props => <Input {...props} value={topic} maxLength={100} onChange={event => setTopic(event.target.value)} placeholder="Tên chủ đề đầy đủ" />}</FormField>
    {kind === 'incidents' && <FormField label="Tình trạng">{props => <Select {...props} value={active} onChange={event => setActive(event.target.value)}><option value="true">Đang diễn ra</option><option value="false">Đã khắc phục</option><option value="">Tất cả</option></Select>}</FormField>}
    <Button type="submit">Tra cứu</Button>
  </form>
}
function EntryCard({ row, kind }: { row: PublicEntry; kind: CommunityKind }) {
  return <article className="community-card"><div className="community-links"><span className="ticket-badge">{row.topic}</span>{row.status && <span className="ticket-badge">{incidentLabels[row.status]}</span>}</div>
    <h2><Link to={`/${kind}/${row.id}`}>{row.title}</Link></h2><p>{row.summary}</p>
    <p className="field-hint">{formatDateTime(row.publishedAt)} · {kind === 'incidents' ? `${row.affectedCount} người xác nhận bị ảnh hưởng` : `${row.helpfulCount} đánh giá hữu ích`}</p></article>
}
export function CommunityPublicDetail({ kind }: { kind: CommunityKind }) {
  const { id = '' } = useParams()
  return isGuid(id) ? <PublicDetail key={`${kind}:${id}`} kind={kind} id={id} /> : <EmptyState title="Không tìm thấy nội dung" />
}
function PublicDetail({ kind, id }: { kind: CommunityKind; id: string }) {
  const query = usePublicEntry(kind, id)
  if (query.isPending) return <LoadingState />
  if (query.error) return <ErrorState error={query.error} onRetry={() => { void query.refetch() }} />
  const row = query.data
  return <article className="community-page"><PageTitle title={row.title} /><Link to={`/${kind}`}>← {communityLabels[kind]}</Link>
    <header className="community-heading"><p className="eyebrow">{row.topic}</p><h1>{row.title}</h1><p>{row.summary}</p>
      <p className="field-hint">Cập nhật {formatDateTime(row.publishedAt)} · Phiên bản công khai {row.revision}</p>
      {row.status && <p className="ticket-badge">{incidentLabels[row.status]}</p>}</header>
    <div className="community-content">{row.content}</div>
    <CommunityParticipationPanel kind={kind} row={row} />
    {kind === 'incidents' && <IncidentTimeline id={id} />}
  </article>
}
function CommunityParticipationPanel({ kind, row }: { kind: CommunityKind; row: PublicEntry }) {
  const { status } = useAuth()
  return status === 'authenticated' ? <SignedInParticipation key={`${kind}:${row.id}`} kind={kind} row={row} />
    : <p className="community-card"><Link to={`/login?returnTo=${encodeURIComponent(`/${kind}/${row.id}`)}`}>Đăng nhập</Link> để {kind === 'incidents' ? 'xác nhận bạn cũng bị ảnh hưởng.' : 'đánh giá hữu ích hoặc ghi nhận đã tự giải quyết.'}</p>
}
function SignedInParticipation({ kind, row }: { kind: CommunityKind; row: PublicEntry }) {
  const { api, scope } = useCommunityApi()
  const queries = useQueryClient()
  const command = useApiCommand()
  const participation = useQuery({ ...communityReadOptions, queryKey: ['community-participation', scope, kind, row.id], queryFn: ({ signal }) => api.participation(kind, row.id, signal) })
  async function submit(value: boolean, solved: boolean) {
    await command.run(signal => api.participate(kind, row.id, value, solved, signal), async () => {
      await participation.refetch(); await queries.invalidateQueries({ queryKey: publicCommunityKey })
    })
  }
  return <section className="community-card"><h2>{kind === 'incidents' ? `${row.affectedCount} người đã xác nhận bị ảnh hưởng` : `${row.helpfulCount} người thấy hữu ích · ${row.solvedCount} người đã tự giải quyết`}</h2>
    {participation.isPending ? <LoadingState /> : participation.error ? <ErrorState error={participation.error} /> : <div className="community-links">
      {kind === 'incidents' ? <Button disabled={command.disabled || (!participation.data.affected && row.status === 'Resolved')} onClick={() => { void submit(!participation.data.affected, false) }}>{participation.data.affected ? 'Gỡ xác nhận bị ảnh hưởng' : 'Tôi cũng gặp vấn đề này'}</Button> : <>
        <Button disabled={command.disabled} onClick={() => { void submit(true, participation.data.solved) }}>{participation.data.helpful ? 'Đã đánh giá hữu ích' : 'Hướng dẫn hữu ích'}</Button>
        <Button variant="secondary" disabled={command.disabled} onClick={() => { void submit(true, !participation.data.solved) }}>{participation.data.solved ? 'Gỡ xác nhận tự giải quyết' : 'Tôi đã tự giải quyết được'}</Button>
        <Button variant="secondary" disabled={command.disabled} onClick={() => { void submit(false, false) }}>Chưa giúp giải quyết</Button>
      </>}
    </div>}
    {command.error && <ErrorState error={command.error} />}{command.uncertain && <p role="alert">Chưa xác định được kết quả. Tải lại trang để kiểm tra trước khi gửi tiếp.</p>}
  </section>
}
function IncidentTimeline({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useQuery({ ...communityReadOptions, queryKey: [...publicCommunityKey, 'updates', id, page], queryFn: ({ signal }) => publicCommunityApi.updates(id, page, signal), refetchInterval: 30000 })
  return <section className="community-card"><h2>Tiến trình xử lý</h2>{query.isPending ? <LoadingState /> : query.error ? <ErrorState error={query.error} /> : <>
    <ol className="community-timeline">{query.data.items.map(update => <li key={update.id}><p><strong>{incidentLabels[update.status]}</strong> · {formatDateTime(update.createdAt)}</p><p className="community-content">{update.message}</p></li>)}</ol>
    <Pagination page={page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={setPage} />
  </>}</section>
}
