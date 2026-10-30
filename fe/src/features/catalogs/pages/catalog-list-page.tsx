import { Link, useSearchParams } from 'react-router-dom'
import { PageTitle } from '../../../shared/components/page-title'
import { DataTable } from '../../../shared/components/data-table'
import { Pagination } from '../../../shared/components/pagination'
import { Button } from '../../../shared/components/button'
import { EmptyState } from '../../../shared/components/async-state'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useCatalogDetail, useCatalogList, useTeamAccess } from '../hooks/use-catalogs'
import { CatalogFilters } from '../components/catalog-filters'
import { CatalogError } from '../components/catalog-state'
import { catalogLabels, catalogSearchParams, parseCatalogSearch } from '../lib/catalog-search'
import type { CatalogKind, CatalogSearch } from '../types/catalog'

export function CatalogListPage({ kind }: { kind: CatalogKind }) {
  const [params, setParams] = useSearchParams()
  const parsed = parseCatalogSearch(params, kind)
  return <section className="account-page catalog-page"><PageTitle title={catalogLabels[kind].title} />
    <p className="eyebrow">Quản trị dữ liệu</p><h1>{catalogLabels[kind].title}</h1>
    <p>{kind === 'teams' ? 'Các nhóm trong phạm vi quản trị hiện tại. Trưởng nhóm chỉ quản lý nhóm có membership lãnh đạo hợp lệ.'
      : kind === 'categories' ? 'Duyệt từng nhánh, tìm theo tên hoặc xem tất cả cấp. Danh mục đã tắt vẫn được giữ cho lịch sử.' : 'Quản lý thông tin và trạng thái phòng ban, giữ liên kết lịch sử của tài khoản và nhóm.'}</p>
    {parsed.value ? <CatalogResults key={`${kind}:${params}`} kind={kind} search={parsed.value} onChange={value => setParams(catalogSearchParams(value))} />
      : <EmptyState title="Bộ lọc chưa hợp lệ" description={parsed.error} action={<Button onClick={() => setParams({})}>Xóa bộ lọc</Button>} />}
  </section>
}
function CatalogResults({ kind, search, onChange }: { kind: CatalogKind; search: CatalogSearch; onChange: (value: CatalogSearch) => void }) {
  const query = useCatalogList(kind, search)
  const access = useTeamAccess(undefined, kind === 'teams')
  const path = catalogLabels[kind].path
  return <>
    {(kind !== 'teams' || access.data?.canCreate) && <p><Link className="button button--primary" to={`${path}/new`}>Tạo {catalogLabels[kind].singular}</Link></p>}
    {kind === 'teams' && access.error && <CatalogError error={access.error} onRetry={() => { void access.refetch() }} />}
    {kind === 'categories' && <CategoryBranch parentId={search.parentId} />}
    <CatalogFilters kind={kind} value={search} onApply={onChange} />
    {query.error ? <CatalogError error={query.error} onRetry={() => { void query.refetch() }} /> : <>
      <DataTable caption={catalogLabels[kind].title} loading={query.isFetching} rows={query.data?.items ?? []} rowKey={row => row.id} columns={[
        { key: 'name', header: 'Tên', render: row => <><Link to={`${path}/${row.id}`}>{row.name}</Link>{row.description && <p className="field-hint">{row.description}</p>}</> },
        { key: 'context', header: kind === 'teams' ? 'Phòng ban' : kind === 'categories' ? 'Phân cấp' : 'Ngày tạo', render: row => 'departmentName' in row
          ? <>{row.departmentName}{!row.departmentIsActive && <p className="field-hint">Phòng ban đã vô hiệu hóa</p>}</>
          : 'parentId' in row ? <>{row.parentName || 'Danh mục gốc'}{row.hasChildren && <p><Link to={`${path}?parentId=${row.id}&rootsOnly=false`}>Xem nhánh {row.name}</Link></p>}</> : formatDateTime(row.createdAt) },
        { key: 'active', header: 'Trạng thái', render: row => <><span className={`account-status${row.isActive ? ' is-active' : ''}`}>{row.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</span>
          {'isAvailable' in row && row.isActive && !row.isAvailable && <p className="field-hint">Nhánh cha không khả dụng</p>}</> },
      ]} />
      {!query.isFetching && query.data && <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={page => onChange({ ...search, page })} />}
    </>}
  </>
}
function CategoryBranch({ parentId }: { parentId?: string }) {
  const parent = useCatalogDetail('categories', parentId ?? '', !!parentId)
  return <nav className="catalog-breadcrumb" aria-label="Cây danh mục"><Link to="/admin/categories?rootsOnly=true">Danh mục gốc</Link>
    {parentId && (parent.error ? <CatalogError error={parent.error} onRetry={() => { void parent.refetch() }} /> : parent.data && <>
      {parent.data.parentId && <Link to={`/admin/categories?parentId=${parent.data.parentId}&rootsOnly=false`}>Lên một cấp</Link>}
      <span aria-current="page">Nhánh {parent.data.name}</span><Link to={`/admin/categories/${parent.data.id}`}>Thông tin danh mục cha</Link>
    </>)}
  </nav>
}
