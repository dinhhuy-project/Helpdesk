import { Link, useSearchParams } from 'react-router-dom'
import { useAudit, useRoleAccess } from '../hooks/use-accounts'
import { AccountError } from '../components/account-state'
import { PageTitle } from '../../../shared/components/page-title'
import { LoadingState, EmptyState } from '../../../shared/components/async-state'
import { DataTable } from '../../../shared/components/data-table'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { ApiError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'

const actions: Record<string, string> = { UserCreated: 'Tạo tài khoản', UserUpdated: 'Cập nhật thông tin', UserStatusChanged: 'Thay đổi trạng thái',
  UserRolesChanged: 'Thay đổi vai trò', RoleGrantsChanged: 'Thay đổi grant', PasswordChanged: 'Tự đổi mật khẩu', AccountAdministrationEnabled: 'Bật quản trị tài khoản' }
export function AccountAuditPage() {
  const [params, setParams] = useSearchParams()
  const raw = params.get('page') ?? '1'
  const valid = params.getAll('page').length <= 1 && /^\d+$/.test(raw) && Number(raw) >= 1 && Number(raw) <= 1_000_000
  return <section className="account-page"><PageTitle title="Nhật ký quản trị" /><p className="eyebrow">Audit</p><h1>Nhật ký quản trị</h1>
    <p>Thời gian hiển thị theo múi giờ của bạn. Actor trống là thao tác từ operator; mã định danh được giữ theo bản ghi audit.</p><p><Link to="/admin/roles">Về vai trò và quyền</Link></p>
    {valid ? <AuditResults page={Number(raw)} onPage={page => setParams(page === 1 ? {} : { page: String(page) })} />
      : <EmptyState title="Phân trang không hợp lệ" action={<Button onClick={() => setParams({})}>Về trang đầu</Button>} />}
  </section>
}
function AuditResults({ page, onPage }: { page: number; onPage: (page: number) => void }) {
  const access = useRoleAccess()
  const query = useAudit(page, access.data?.canReadAudit === true)
  if (access.error || query.error) return <AccountError error={access.error ?? query.error} onRetry={() => { void access.refetch(); if (access.data?.canReadAudit) void query.refetch() }} />
  if (!access.data) return <LoadingState />
  if (!access.data.canReadAudit) return <AccountError error={new ApiError('Không có quyền đọc audit.', 'http', 403)} />
  return <><DataTable caption="Nhật ký quản trị Identity" loading={query.isFetching || query.isPending} rows={query.data?.items ?? []} rowKey={row => row.id} columns={[
    { key: 'date', header: 'Thời gian', render: row => formatDateTime(row.createdAt) },
    { key: 'action', header: 'Thao tác', render: row => <>{actions[row.action] ?? row.action}<p className="field-hint">{row.action}</p></> },
    { key: 'actor', header: 'Actor ID', render: row => row.actorId || 'Operator' },
    { key: 'target', header: 'Đối tượng', render: row => <>{row.targetUserId && <p>User: {row.targetUserId}</p>}{row.targetRoleId && <p>Role: {row.targetRoleId}</p>}{!row.targetUserId && !row.targetRoleId && '—'}</> },
    { key: 'data', header: 'Thay đổi', render: row => row.data ? <details><summary>Xem metadata</summary><pre className="account-audit-data">{row.data}</pre></details> : '—' },
  ]} />{!query.isFetching && query.data && <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={onPage} />}</>
}
