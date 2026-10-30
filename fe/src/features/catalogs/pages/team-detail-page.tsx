import { useState } from 'react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../auth/hooks/use-auth'
import { ApiError, toApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import { PageTitle } from '../../../shared/components/page-title'
import { LoadingState, EmptyState, ErrorState } from '../../../shared/components/async-state'
import { Button } from '../../../shared/components/button'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { catalogKeys, useCatalogApi, useCatalogDetail, useTeamAccess, useTeamMembers } from '../hooks/use-catalogs'
import { parseMemberSearch } from '../lib/catalog-search'
import { CatalogEditor } from '../components/catalog-editor'
import type { CatalogEditorDraft } from '../components/catalog-editor'
import { CatalogStatusAction } from '../components/catalog-status-action'
import { CatalogError, CatalogFeedback } from '../components/catalog-state'
import { verifyCatalogReceipt } from '../lib/catalog-receipt'
import { TeamMembers } from '../components/team-members'
import type { MemberAction } from '../components/team-members'
import type { MembershipResult, MemberSearch, SupportTeam, TeamAccess } from '../types/catalog'

export function TeamDetailPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <TeamDetail key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} />
    : <CatalogError error={new ApiError('ID không hợp lệ.', 'http', 404)} />
}
function TeamDetail({ id }: { id: string }) {
  const access = useTeamAccess(id)
  const query = useCatalogDetail('teams', id, !!access.data && !access.error)
  const [revision, setRevision] = useState(0)
  const reload = async () => {
    const results = await Promise.all([access.refetch(), query.refetch()])
    if (results.every(result => !result.error && result.data)) setRevision(value => value + 1)
  }
  if (access.error || query.error) return <CatalogError error={access.error ?? query.error} onRetry={() => { void reload() }} />
  if (!query.data || !access.data) return <LoadingState label="Đang kiểm tra nhóm và phạm vi quản trị…" />
  return <TeamWorkspace key={revision} initial={query.data} latestVersion={query.data.version} access={access.data} onReload={() => { void reload() }} />
}
function TeamWorkspace({ initial, latestVersion, access, onReload }: { initial: SupportTeam; latestVersion: number; access: TeamAccess; onReload: () => void }) {
  const [team, setTeam] = useState(initial)
  const [formRevision, setFormRevision] = useState(0)
  const [saved, setSaved] = useState(false)
  const [needsReload, setNeedsReload] = useState(false)
  const [reloadFailure, setReloadFailure] = useState<ApiError | null>(null)
  const [params, setParams] = useSearchParams()
  const search = parseMemberSearch(params)
  const members = useTeamMembers(team.id, search.value ?? { page: 1, pageSize: 20 }, !!search.value)
  const { api, scope } = useCatalogApi()
  const queries = useQueryClient()
  const command = useApiCommand()
  const location = useLocation()
  function accept(value: SupportTeam) {
    setTeam(value); setSaved(true)
    queries.setQueryData(catalogKeys.detail(scope, 'teams', team.id), value)
  }
  function changeSearch(value: MemberSearch) {
    const next = new URLSearchParams(params)
    for (const key of ['memberPage', 'memberPageSize', 'memberIsActive']) next.delete(key)
    if (value.page !== 1) next.set('memberPage', String(value.page))
    if (value.pageSize !== 20) next.set('memberPageSize', String(value.pageSize))
    if (value.isActive !== undefined) next.set('memberIsActive', String(value.isActive))
    setParams(next)
  }
  async function save(draft: CatalogEditorDraft) {
    const result = await command.run(async signal => verifyCatalogReceipt(await api.update('teams', team.id,
      { name: draft.name, description: draft.description, version: team.version }, signal), team.id))
    if (result) { accept(result); setFormRevision(value => value + 1) }
  }
  async function memberChange(action: MemberAction) {
    if (action.kind === 'remove') {
      const removed = await command.run(async signal => { await api.removeMember(team.id, action.member.userId, action.member.version, action.teamVersion, signal); return true })
      if (!removed) return
      // DELETE committed with 204: a subsequent read failure must never replay the write.
      setSaved(true); setNeedsReload(true); setReloadFailure(null)
      try {
        const updated = await api.detail('teams', team.id)
        if (!command.isCurrent()) return
        accept(updated)
        const result = await members.refetch()
        if (result.error) throw result.error
        if (command.isCurrent()) setNeedsReload(false)
      } catch (failure) { if (command.isCurrent()) setReloadFailure(toApiError(failure)) }
      return
    }
    const userId = action.kind === 'add' ? action.candidate.id : action.member.userId
    const result = await command.run(async signal => {
      let receipt: MembershipResult
      if (action.kind === 'add') receipt = await api.addMember(team.id, userId, action.teamVersion, signal)
      else if (action.kind === 'status') receipt = await api.memberStatus(team.id, userId, !action.member.isActive, action.member.version, action.teamVersion, signal)
      else receipt = await api.leadership(team.id, userId, !action.member.isLeader, action.member.version, action.teamVersion, signal)
      verifyCatalogReceipt(receipt?.team, team.id)
      if (receipt.member?.userId !== userId || !receipt.member.version) throw new ApiError('Chưa xác nhận được membership đã lưu.', 'unexpected')
      return receipt
    })
    if (result) { accept(result.team); void members.refetch() }
  }
  return <section className="account-page catalog-page"><PageTitle title={team.name} /><p className="eyebrow">Nhóm hỗ trợ</p><h1>{team.name}</h1><p><Link to="/admin/teams">Về danh sách nhóm</Link></p>
    {(saved || location.state?.catalogCreated === true) && <p className="success-message" role="status">{saved ? 'Đã lưu thay đổi nhóm hoặc thành viên.' : 'Đã tạo nhóm hỗ trợ.'}</p>}
    <dl className="account-summary panel"><div><dt>Phòng ban</dt><dd>{team.departmentName}{!team.departmentIsActive && <p>Phòng ban đã vô hiệu hóa</p>}</dd></div>
      <div><dt>Trạng thái nhóm</dt><dd>{team.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</dd></div></dl>
    {latestVersion !== team.version && <p role="status">Nhóm đã thay đổi. Bản nhập được giữ lại; tải lại cả nhóm và thành viên trước khi lưu.</p>}
    <CatalogFeedback command={command} onReload={onReload} />
    {needsReload && <div role="alert"><p>Thao tác gỡ đã thành công. Cần tải lại nhóm và thành viên trước khi sửa tiếp.</p>{reloadFailure && <ErrorState error={reloadFailure} onRetry={onReload} />}</div>}
    <div className="panel account-form"><h2>Thông tin nhóm</h2><fieldset className="catalog-form-frame" disabled={needsReload}>
      <CatalogEditor key={formRevision} kind="teams" value={team} command={command} onSave={save} />
    </fieldset><p><Button variant="secondary" disabled={command.pending} onClick={onReload}>Tải lại nhóm và thành viên</Button></p>
      <CatalogStatusAction kind="teams" resource={team} allowed={access.canChangeStatus} disabled={command.disabled || needsReload} pending={command.pending} onConfirm={async () => {
        const result = await command.run(async signal => verifyCatalogReceipt(await api.status('teams', team.id, !team.isActive, team.version, signal), team.id))
        if (result) { accept(result); void queries.invalidateQueries({ queryKey: catalogKeys.resource(scope, 'teams', team.id) }) }
      }} />
    </div>
    {search.value ? <TeamMembers team={team} access={access} search={search.value} page={members.data} loading={members.isFetching} error={members.error}
      disabled={command.disabled || needsReload} pending={command.pending} onSearch={changeSearch} onRetry={() => { void members.refetch() }} onConfirm={memberChange} />
      : <EmptyState title="Bộ lọc thành viên chưa hợp lệ" description={search.error} action={<Button onClick={() => changeSearch({ page: 1, pageSize: 20 })}>Xóa bộ lọc thành viên</Button>} />}
  </section>
}
