import { useState } from 'react'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { FormField, Select } from '../../../shared/components/form-field'
import { DataTable } from '../../../shared/components/data-table'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { MemberCandidatePicker } from './catalog-lookups'
import { CatalogError } from './catalog-state'
import type { PagedResult } from '../../../shared/api/pagination'
import type { MemberSearch, SupportTeam, TeamAccess, TeamMember, TeamMemberCandidate } from '../types/catalog'

export type MemberAction = { kind: 'add'; candidate: TeamMemberCandidate; teamVersion: number }
  | { kind: 'status' | 'remove' | 'leadership'; member: TeamMember; teamVersion: number }
export function TeamMembers({ team, access, search, page, loading, error, disabled, pending, onSearch, onRetry, onConfirm }: {
  team: SupportTeam; access: TeamAccess; search: MemberSearch; page?: PagedResult<TeamMember>; loading: boolean; error: unknown
  disabled: boolean; pending: boolean; onSearch: (search: MemberSearch) => void; onRetry: () => void; onConfirm: (action: MemberAction) => Promise<void>
}) {
  const [action, setAction] = useState<MemberAction | null>(null)
  const activeTeam = team.isActive && team.departmentIsActive
  return <section className="panel catalog-members" aria-labelledby="team-members-title"><div className="page-heading"><div><h2 id="team-members-title">Thành viên và trưởng nhóm</h2>
    <p>Tham gia nhóm không tự cấp quyền xử lý Ticket. Ngừng tham gia giữ ngày tham gia, vai trò trưởng nhóm và lịch sử.</p></div>
    <MemberCandidatePicker teamId={team.id} disabled={disabled || !access.canAddMembers || !activeTeam} onSelect={candidate => setAction({ kind: 'add', candidate, teamVersion: team.version })} />
  </div>
    <div className="account-actions"><FormField label="Trạng thái tham gia">{props => <Select {...props} value={search.isActive === undefined ? '' : String(search.isActive)}
      onChange={event => onSearch({ ...search, page: 1, isActive: event.target.value ? event.target.value === 'true' : undefined })}>
      <option value="">Tất cả</option><option value="true">Đang tham gia</option><option value="false">Đã ngừng tham gia</option>
    </Select>}</FormField><FormField label="Thành viên mỗi trang">{props => <Select {...props} value={search.pageSize} onChange={event => onSearch({ ...search, page: 1, pageSize: Number(event.target.value) })}>
      {[...new Set([10,20,50,100,search.pageSize])].sort((a,b) => a-b).map(n => <option key={n} value={n}>{n}</option>)}
    </Select>}</FormField></div>
    {error ? <CatalogError error={error} onRetry={onRetry} /> : <>
      <DataTable caption="Thành viên nhóm hỗ trợ" loading={loading} rows={page?.items ?? []} rowKey={row => row.userId} columns={[
        { key: 'name', header: 'Thành viên', render: row => <>{row.displayName}{!row.userIsActive && <p className="field-hint">Tài khoản đã vô hiệu hóa</p>}</> },
        { key: 'role', header: 'Vai trò trong nhóm', render: row => row.isLeader ? 'Trưởng nhóm' : 'Thành viên' },
        { key: 'status', header: 'Tham gia', render: row => <>{row.isActive ? 'Đang tham gia' : 'Đã ngừng tham gia'}{row.isActive && (!activeTeam || !row.userIsActive) && <p className="field-hint">Chưa có hiệu lực vận hành</p>}</> },
        { key: 'joined', header: 'Tham gia từ', render: row => formatDateTime(row.joinedAt) },
        { key: 'actions', header: 'Thao tác', render: row => <div className="catalog-member-actions">
          {!row.isLeader || access.canManageLeadership ? <>
            <Button variant="secondary" disabled={disabled || (!row.isActive && (!activeTeam || !row.userIsActive))} onClick={() => setAction({ kind: 'status', member: row, teamVersion: team.version })}>
              {row.isActive ? 'Ngừng tham gia' : 'Kích hoạt lại'}</Button>
            {row.isActive && <Button variant="danger" disabled={disabled} onClick={() => setAction({ kind: 'remove', member: row, teamVersion: team.version })}>Gỡ khỏi nhóm</Button>}
          </> : <span className="field-hint">Chỉ quản trị hệ thống được thay đổi việc tham gia của trưởng nhóm.</span>}
          {access.canManageLeadership && <Button variant="secondary" disabled={disabled || (!row.isLeader && !row.canBeLeader)}
            onClick={() => setAction({ kind: 'leadership', member: row, teamVersion: team.version })}>{row.isLeader ? 'Gỡ trưởng nhóm' : 'Chỉ định trưởng nhóm'}</Button>}
        </div> },
      ]} />
      {!loading && page && <Pagination page={page.page} pageSize={page.pageSize} totalCount={page.totalCount} onPageChange={number => onSearch({ ...search, page: number })} />}
    </>}
    <Dialog open={!!action} onOpenChange={open => { if (!open && !pending) setAction(null) }} title="Xác nhận thay đổi thành viên"
      description={action ? action.kind === 'add' ? `Thêm ${action.candidate.displayName} vào ${team.name} với tư cách thành viên thường.`
        : action.kind === 'leadership' ? `${action.member.isLeader ? 'Gỡ' : 'Chỉ định'} trưởng nhóm: ${action.member.displayName}. Không thay role toàn hệ thống.`
          : `${action.kind === 'remove' || action.member.isActive ? 'Ngừng' : 'Khôi phục'} tham gia: ${action.member.displayName}. Giữ dữ liệu lịch sử.` : undefined}>
      <Button disabled={disabled || !action} onClick={async () => { if (action) { await onConfirm(action); setAction(null) } }}>{pending ? 'Đang cập nhật…' : 'Xác nhận thay đổi thành viên'}</Button>
    </Dialog>
  </section>
}
