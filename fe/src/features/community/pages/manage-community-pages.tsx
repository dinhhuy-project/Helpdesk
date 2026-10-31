import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '../../../shared/components/button'
import { FormField, Input, Select, Textarea } from '../../../shared/components/form-field'
import { LoadingState, ErrorState, EmptyState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { Pagination } from '../../../shared/components/pagination'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { isGuid } from '../../../shared/lib/identifier'
import { Permission } from '../../auth/lib/permissions'
import { communityLabels, incidentLabels, type CommunityKind, type ManagedEntry, type EntryDraft } from '../api/community-api'
import { communityReadOptions, publicCommunityKey, useCommunityApi } from '../hooks/use-community'
import { BookStackImport } from '../components/knowledge-sources'

export function CommunityManageList({ kind }: { kind: CommunityKind }) {
  const { api, scope, user } = useCommunityApi()
  const [params, setParams] = useSearchParams()
  const rawPage = Number(params.get('page') || 1)
  const page = Number.isSafeInteger(rawPage) && rawPage > 0 && rawPage <= 100000 ? rawPage : 1
  const search = params.get('search') ?? ''
  const query = useQuery({ ...communityReadOptions, queryKey: ['community-admin', scope, kind, page, search], queryFn: ({ signal }) => api.list(kind, { page, search }, signal) })
  const canManage = user?.permissions.includes(kind === 'knowledge' ? Permission.KnowledgeManage : Permission.IncidentManage)
  return <section className="community-page"><PageTitle title={`Quản trị ${communityLabels[kind]}`} /><h1>{communityLabels[kind]}</h1>
    <p>Bản soạn thảo được lưu riêng. Chỉ người có quyền xuất bản mới đưa nội dung ra cổng tra cứu công khai.</p>
    <div className="community-links">{canManage && <Link className="button button--primary" to={`/admin/${kind}/new`}>Soạn nội dung mới</Link>}<Link to={`/${kind}`}>Xem cổng công khai</Link></div>
    {kind === 'knowledge' && canManage && <BookStackImport key={scope} />}
    <form className="community-filters" onSubmit={event => { event.preventDefault(); const value = new FormData(event.currentTarget).get('search'); setParams(typeof value === 'string' && value.trim() ? { search: value.trim() } : {}) }}>
      <FormField label="Tìm trong bản soạn thảo">{props => <Input {...props} name="search" type="search" defaultValue={search} maxLength={200} />}</FormField><Button type="submit">Tìm kiếm</Button></form>
    {query.isPending ? <LoadingState /> : query.error ? <ErrorState error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      {!query.data.items.length && <EmptyState title="Chưa có nội dung" />}
      <div className="community-grid">{query.data.items.map(row => <article className="community-card" key={row.id}>
        <h2><Link to={`/admin/${kind}/${row.id}`}>{row.title}</Link></h2><p>{row.summary}</p><p>{row.isPublished ? `Đang công khai · phiên bản ${row.publicRevision}` : 'Chưa công khai / đã thu hồi'}</p>
      </article>)}</div><Pagination page={page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={next => { const p = new URLSearchParams(params); p.set('page', String(next)); setParams(p) }} />
    </>}
  </section>
}
export function CommunityManageDetail({ kind }: { kind: CommunityKind }) {
  const { id } = useParams()
  const { api, scope } = useCommunityApi()
  const query = useQuery({ ...communityReadOptions, queryKey: ['community-admin', scope, kind, id], enabled: isGuid(id), queryFn: ({ signal }) => api.get(kind, id ?? '', signal) })
  if (!id) return <CommunityEditor key={`${scope}:${kind}:new`} kind={kind} />
  if (!isGuid(id)) return <EmptyState title="Không tìm thấy nội dung" />
  if (query.isPending) return <LoadingState />
  if (query.error) return <ErrorState error={query.error} onRetry={() => { void query.refetch() }} />
  return <CommunityEditor key={`${scope}:${kind}:${id}`} kind={kind} initial={query.data} />
}
function CommunityEditor({ kind, initial }: { kind: CommunityKind; initial?: ManagedEntry }) {
  const { api, user } = useCommunityApi()
  const command = useApiCommand()
  const queries = useQueryClient()
  const navigate = useNavigate()
  const [row, setRow] = useState(initial)
  const [draft, setDraft] = useState<EntryDraft>({ title: initial?.title ?? '', summary: initial?.summary ?? '', content: initial?.content ?? '', topic: initial?.topic ?? '', status: kind === 'incidents' ? initial?.status ?? 'Investigating' : null, version: initial?.version ?? 0 })
  const [reviewed, setReviewed] = useState(false)
  const [message, setMessage] = useState('')
  const [notice, setNotice] = useState('')
  const [reloadError, setReloadError] = useState<unknown>()
  const canManage = user?.permissions.includes(kind === 'knowledge' ? Permission.KnowledgeManage : Permission.IncidentManage) ?? false
  const canPublish = user?.permissions.includes(kind === 'knowledge' ? Permission.KnowledgePublish : Permission.IncidentPublish) ?? false
  const dirty = !row || ['title', 'summary', 'content', 'topic', 'status'].some(key => draft[key as keyof EntryDraft] !== row[key as keyof ManagedEntry])
  function change(key: keyof EntryDraft, value: string) { setDraft(previous => ({ ...previous, [key]: value })); setReviewed(false) }
  async function committed(result: ManagedEntry, savedDraft = false) {
    setRow(result); setDraft(previous => savedDraft ? { title: result.title, summary: result.summary, content: result.content,
      topic: result.topic, status: result.status, version: result.version } : { ...previous, version: result.version }); setReviewed(false)
    await queries.invalidateQueries({ queryKey: ['community-admin'] }); await queries.invalidateQueries({ queryKey: publicCommunityKey })
  }
  async function reloadVersion() {
    if (!row) return
    try {
      const latest = await api.get(kind, row.id)
      setRow(latest); setDraft(previous => ({ ...previous, version: latest.version })); setReviewed(false); setReloadError(undefined)
      command.resetAfterVerification(); setNotice('Đã tải phiên bản mới và giữ nội dung đang nhập. So sánh với bản công khai, kiểm tra lại trước khi lưu hoặc xuất bản.')
    } catch (error) { setReloadError(error) }
  }
  if (!initial && !canManage) return <EmptyState title="Bạn không có quyền soạn nội dung" />
  return <section className="community-page"><PageTitle title={row ? row.title : 'Soạn nội dung mới'} /><Link to={`/admin/${kind}`}>← {communityLabels[kind]}</Link>
    <h1>{row ? 'Biên soạn và xuất bản' : 'Soạn nội dung mới'}</h1>
    <p>Nội dung xuất bản có thể được đọc mà không cần đăng nhập. Hãy loại bỏ thông tin cá nhân, thông tin nội bộ và bí mật trước khi duyệt.</p>
    {row && <p className="field-hint">{row.isPublished ? `Đang công khai phiên bản ${row.publicRevision}. Lưu bản soạn không đổi bản công khai.` : 'Nội dung chưa được công khai.'}</p>}
    {notice && <p role="status">{notice}</p>}
    <form className="community-card community-editor" onSubmit={async event => {
      event.preventDefault(); const result = await command.run(signal => api.save(kind, row?.id, draft, signal))
      if (result) { await committed(result, true); setNotice('Đã lưu bản soạn. Nội dung chỉ công khai sau khi xuất bản.'); if (!row) void navigate(`/admin/${kind}/${result.id}`, { replace: true }) }
    }}>
      <FormField label="Tiêu đề" required>{props => <Input {...props} maxLength={200} value={draft.title} disabled={!canManage || command.pending} onChange={event => change('title', event.target.value)} />}</FormField>
      <FormField label="Chủ đề" required>{props => <Input {...props} maxLength={100} value={draft.topic} disabled={!canManage || command.pending} onChange={event => change('topic', event.target.value)} />}</FormField>
      <FormField label="Tóm tắt" required>{props => <Textarea {...props} maxLength={1000} value={draft.summary} disabled={!canManage || command.pending} onChange={event => change('summary', event.target.value)} />}</FormField>
      <FormField label={kind === 'knowledge' ? 'Nội dung hướng dẫn' : 'Mô tả ảnh hưởng và hướng xử lý'} required hint="Văn bản thuần; chia bước rõ ràng để người đọc có thể tự thực hiện.">{props => <Textarea {...props} maxLength={30000} value={draft.content} disabled={!canManage || command.pending} onChange={event => change('content', event.target.value)} />}</FormField>
      {kind === 'incidents' && <FormField label="Trạng thái sẽ xuất bản">{props => <Select {...props} value={draft.status ?? 'Investigating'} disabled={!canManage || command.pending} onChange={event => change('status', event.target.value)}>{Object.entries(incidentLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select>}</FormField>}
      {canManage && <Button type="submit" disabled={command.disabled || !dirty}>Lưu bản soạn</Button>}
    </form>
    {row?.sourceTicketId && <p className="field-hint">Nguồn biên soạn: <Link to={`/tickets/${row.sourceTicketId}`}>Ticket trong phạm vi được phép</Link>. Liên kết này không xuất hiện trên cổng công khai.</p>}
    {row && <section className="community-card"><h2>Duyệt công khai</h2>
      <p>Bản nội dung bên trên là bản xem trước. {dirty ? 'Có thay đổi chưa lưu; lưu bản soạn trước khi xuất bản.' : 'Bản soạn đã lưu có thể được xuất bản theo quyền của bạn.'}</p>
      <details><summary>Xem trước nội dung sẽ công khai</summary><h3>{draft.title}</h3><p>{draft.summary}</p><div className="community-content">{draft.content}</div></details>
      {canPublish && <>
        {kind === 'incidents' && <FormField label="Thông điệp cập nhật tiến trình" hint="Hiển thị công khai trong timeline. Để trống để dùng tóm tắt.">{props => <Textarea {...props} maxLength={1000} value={message} onChange={event => { setMessage(event.target.value); setReviewed(false) }} />}</FormField>}
        <label className="community-review"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} />Tôi đã kiểm tra nội dung và xác nhận được phép công khai cho mọi người.</label>
        <div className="community-links"><Button disabled={command.disabled || dirty || !reviewed} onClick={async () => {
          const result = await command.run(signal => api.publish(kind, row, message, signal)); if (result) { await committed(result); setMessage(''); setNotice('Đã xuất bản nội dung công khai.') }
        }}>Xuất bản công khai</Button>
        {row.isPublished && <Button variant="danger" disabled={command.disabled} onClick={async () => {
          if (!window.confirm('Thu hồi nội dung khỏi cổng tra cứu công khai?')) return
          const result = await command.run(signal => api.withdraw(kind, row, signal)); if (result) { await committed(result); setNotice('Đã thu hồi khỏi cổng công khai.') }
        }}>Thu hồi công khai</Button>}</div>
      </>}
      {row.isPublished && <p><Link to={`/${kind}/${row.id}`}>Kiểm tra bản đang công khai</Link></p>}
    </section>}
    {command.error && <ErrorState error={command.error} />}
    {command.error?.status === 409 && <Button variant="secondary" onClick={() => { void reloadVersion() }}>Giữ nội dung nhập và dùng phiên bản mới nhất</Button>}
    {command.uncertain && <p role="alert">Chưa xác định được kết quả thao tác. Không gửi lại tự động; kiểm tra danh sách hoặc tải lại trang để đối soát.</p>}
    {reloadError !== undefined && <ErrorState error={reloadError} />}
  </section>
}
