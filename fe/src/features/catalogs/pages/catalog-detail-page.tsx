import { useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { Permission } from '../../auth/lib/permissions'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import { PageTitle } from '../../../shared/components/page-title'
import { LoadingState } from '../../../shared/components/async-state'
import { Button } from '../../../shared/components/button'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { formatDateTime } from '../../../shared/lib/date-time'
import { catalogKeys, useCatalogApi, useCatalogDetail } from '../hooks/use-catalogs'
import { catalogLabels } from '../lib/catalog-search'
import { CatalogEditor } from '../components/catalog-editor'
import type { CatalogEditorDraft } from '../components/catalog-editor'
import { CatalogStatusAction } from '../components/catalog-status-action'
import { CatalogError, CatalogFeedback } from '../components/catalog-state'
import { verifyCatalogReceipt } from '../lib/catalog-receipt'
import type { Department, TicketCategory } from '../types/catalog'

type Kind = 'departments' | 'categories'
export function CatalogDetailPage({ kind }: { kind: Kind }) {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <CatalogDetail key={`${scope}:${kind}:${id.toLowerCase()}`} kind={kind} id={id.toLowerCase()} />
    : <CatalogError error={new ApiError('ID không hợp lệ.', 'http', 404)} />
}
function CatalogDetail({ kind, id }: { kind: Kind; id: string }) {
  const query = useCatalogDetail(kind, id)
  const [revision, setRevision] = useState(0)
  const reload = async () => {
    const result = await query.refetch()
    if (result.data && !result.error) setRevision(value => value + 1)
  }
  if (query.error) return <CatalogError error={query.error} onRetry={() => { void reload() }} />
  if (!query.data) return <LoadingState />
  return <CatalogWorkspace key={revision} kind={kind} initial={query.data} latestVersion={query.data.version} onReload={() => { void reload() }} />
}
function CatalogWorkspace({ kind, initial, latestVersion, onReload }: { kind: Kind; initial: Department | TicketCategory; latestVersion: number; onReload: () => void }) {
  const [value, setValue] = useState(initial)
  const [formRevision, setFormRevision] = useState(0)
  const [saved, setSaved] = useState(false)
  const { api, scope } = useCatalogApi()
  const { user } = useAuth()
  const queries = useQueryClient()
  const command = useApiCommand()
  const location = useLocation()
  const label = catalogLabels[kind]
  function accept(receipt: Department | TicketCategory, resetForm: boolean) {
    setValue(receipt); setSaved(true)
    queries.setQueryData(catalogKeys.detail(scope, kind, receipt.id), receipt)
    if (resetForm) setFormRevision(revision => revision + 1)
  }
  async function save(draft: CatalogEditorDraft) {
    const body = { name: draft.name, description: draft.description, version: value.version }
    const result = await command.run(async signal => verifyCatalogReceipt(kind === 'categories'
      ? await api.update(kind, value.id, { ...body, parentId: draft.parentId }, signal)
      : await api.update(kind, value.id, body, signal), value.id))
    if (result) accept(result, true)
  }
  return <section className="account-page catalog-page"><PageTitle title={value.name} /><p className="eyebrow">{label.title}</p><h1>{value.name}</h1>
    <p><Link to={label.path}>Về danh sách</Link></p>
    {(saved || location.state?.catalogCreated === true) && <p role="status" className="success-message">{saved ? 'Đã lưu thay đổi.' : 'Đã tạo mới thành công.'}</p>}
    <dl className="account-summary panel"><div><dt>Trạng thái</dt><dd>{value.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</dd></div><div><dt>Ngày tạo</dt><dd>{formatDateTime(value.createdAt)}</dd></div>
      {'isAvailable' in value && <div><dt>Khả dụng cho Ticket mới</dt><dd>{value.isAvailable ? 'Có' : 'Không; danh mục hoặc nhánh cha đã tắt'}</dd></div>}
    </dl>
    {latestVersion !== value.version && <p role="status">Dữ liệu đã thay đổi. Bản nhập hiện tại được giữ lại; tải lại để kiểm tra bản mới.</p>}
    <div className="panel account-form"><h2>Thông tin</h2><CatalogEditor key={formRevision} kind={kind} value={value} command={command} onSave={save} />
      <CatalogFeedback command={command} onReload={onReload} /><p><Button variant="secondary" disabled={command.pending} onClick={onReload}>Tải lại thông tin</Button></p>
      <CatalogStatusAction kind={kind} resource={value} disabled={command.disabled} pending={command.pending} onConfirm={async () => {
        const result = await command.run(async signal => verifyCatalogReceipt(await api.status(kind, value.id, !value.isActive, value.version, signal), value.id))
        if (result) accept(result, false)
      }} />
    </div>
    {kind === 'departments' && user?.permissions.includes(Permission.TeamManage) && <p><Link to={`/admin/teams?departmentId=${value.id}`}>Nhóm thuộc phòng ban này</Link></p>}
    {kind === 'categories' && <p><Link to={`/admin/categories?parentId=${value.id}&rootsOnly=false`}>Xem danh mục con</Link></p>}
  </section>
}
