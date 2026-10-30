import { Link, useSearchParams } from 'react-router-dom'
import { useAccounts } from '../hooks/use-accounts'
import { accountSearchParams, parseAccountSearch } from '../lib/account-search'
import { AccountFilters } from '../components/account-filters'
import { AccountError } from '../components/account-state'
import { PageTitle } from '../../../shared/components/page-title'
import { Button } from '../../../shared/components/button'
import { DataTable } from '../../../shared/components/data-table'
import { Pagination } from '../../../shared/components/pagination'
import { EmptyState } from '../../../shared/components/async-state'
import { formatDateTime } from '../../../shared/lib/date-time'
import { roleLabel } from '../../auth/lib/identity-labels'
import type { AccountSearch } from '../types/account'

export function AccountListPage({ assignment = false }: { assignment?: boolean }) {
  const [params, setParams] = useSearchParams()
  const parsed = parseAccountSearch(params)
  const title = assignment ? 'Gán vai trò cho tài khoản' : 'Tài khoản người dùng'
  return <section className="account-page"><PageTitle title={title} />
    <div className="page-heading"><div><p className="eyebrow">Quản trị</p><h1>{title}</h1>
      <p>{assignment ? 'Chỉ hiển thị tài khoản thuộc phạm vi cấp vai trò của bạn. Không tự thay đổi vai trò của chính mình.' : 'Quản lý thông tin và trạng thái tài khoản. Vai trò được quản lý riêng.'}</p></div>
      {assignment ? <Link to="/admin/roles">Vai trò và quyền</Link> : <Link className="button button--primary" to="/admin/users/new">Tạo tài khoản</Link>}
    </div>
    {!parsed.value ? <EmptyState title="Bộ lọc chưa hợp lệ" description={parsed.error} action={<Button onClick={() => setParams({})}>Xóa bộ lọc</Button>} />
      : <AccountResults key={params.toString()} search={parsed.value} assignment={assignment} onChange={value => setParams(accountSearchParams(value))} />}
  </section>
}
function AccountResults({ search, assignment, onChange }: { search: AccountSearch; assignment: boolean; onChange: (value: AccountSearch) => void }) {
  const query = useAccounts(search, assignment)
  return <><AccountFilters value={search} onApply={onChange} />
    {query.error ? <AccountError error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      <DataTable caption={assignment ? 'Tài khoản có thể gán vai trò' : 'Danh sách tài khoản'} rows={query.data?.items ?? []}
        loading={query.isFetching} rowKey={row => row.id} columns={[
          { key: 'name', header: 'Tài khoản', render: row => <><Link to={assignment ? `/admin/roles/assignments/${row.id}` : `/admin/users/${row.id}`}>{row.displayName}</Link><p className="field-hint">{row.email}</p></> },
          { key: 'roles', header: 'Vai trò hiện tại', render: row => row.roles.map(role => roleLabel(role.name)).join(', ') || 'Chưa có vai trò' },
          { key: 'active', header: 'Trạng thái', render: row => <span className={`account-status${row.isActive ? ' is-active' : ''}`}>{row.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</span> },
          { key: 'date', header: 'Ngày tạo', render: row => formatDateTime(row.createdAt) },
        ]} />
      {!query.isFetching && query.data && <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={page => onChange({ ...search, page })} />}
    </>}
  </>
}
