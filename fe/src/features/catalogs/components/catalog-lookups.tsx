import { useState } from 'react'
import { Button } from '../../../shared/components/button'
import { FormField, Input } from '../../../shared/components/form-field'
import { Dialog } from '../../../shared/components/dialog'
import { Pagination } from '../../../shared/components/pagination'
import { LoadingState } from '../../../shared/components/async-state'
import { useCatalogList, useTeamCandidates, useTeamDepartments } from '../hooks/use-catalogs'
import { CatalogError } from './catalog-state'
import type { LookupOption, TeamMemberCandidate } from '../types/catalog'

export function LookupSearchBox({ label, onSearch }: { label: string; onSearch: (search: string) => void }) {
  const [draft, setDraft] = useState('')
  const search = () => onSearch(draft.trim())
  return <div role="search" aria-label={label} className="account-actions">
    <FormField label={label}>{props => <Input {...props} value={draft} maxLength={200} onChange={event => setDraft(event.target.value)}
      onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); search() } }} />}</FormField>
    <Button variant="secondary" onClick={search}>Tìm kiếm</Button>
  </div>
}
export function TeamDepartmentPicker({ value, disabled, onChange }: { value: LookupOption | null; disabled: boolean; onChange: (value: LookupOption) => void }) {
  const [open, setOpen] = useState(false)
  return <div className="form-field"><span className="account-field-label">Phòng ban</span><p>{value?.name || 'Chưa chọn phòng ban'}</p>
    <Button variant="secondary" disabled={disabled} onClick={() => setOpen(true)}>Chọn phòng ban hoạt động</Button>
    <Dialog open={open} onOpenChange={setOpen} title="Chọn phòng ban hoạt động">
      {open && <TeamDepartmentOptions onSelect={option => { onChange(option); setOpen(false) }} />}
    </Dialog>
  </div>
}
function TeamDepartmentOptions({ onSelect }: { onSelect: (option: LookupOption) => void }) {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const query = useTeamDepartments({ page, pageSize: 20, search: search || undefined })
  return <><LookupSearchBox label="Tìm phòng ban" onSearch={value => { setSearch(value); setPage(1) }} />
    {query.isFetching ? <LoadingState /> : query.error ? <CatalogError error={query.error} onRetry={() => { void query.refetch() }} /> : query.data && <>
      {!query.data.items.length && <p>Không có phòng ban phù hợp trong phạm vi.</p>}
      <ul className="account-options">{query.data.items.map(item => <li key={item.id}><Button variant="secondary" onClick={() => onSelect(item)}>{item.name}</Button></li>)}</ul>
      <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={setPage} />
    </>}
  </>
}
export function CategoryParentPicker({ value, excludeId, disabled, onChange }: {
  value: LookupOption | null; excludeId?: string; disabled: boolean; onChange: (value: LookupOption | null) => void
}) {
  const [open, setOpen] = useState(false)
  return <div className="form-field"><span className="account-field-label">Danh mục cha</span><p>{value?.name || 'Danh mục gốc'}</p>
    <div className="account-actions"><Button variant="secondary" disabled={disabled} onClick={() => setOpen(true)}>Chọn danh mục cha</Button>
      <Button variant="secondary" disabled={disabled || !value} onClick={() => onChange(null)}>Chuyển về danh mục gốc</Button></div>
    <Dialog open={open} onOpenChange={setOpen} title="Chọn danh mục cha khả dụng" description="Mở từng nhánh hoặc tìm theo tên. Server kiểm tra lại tính khả dụng và ngăn vòng lặp khi lưu.">
      {open && <CategoryParentOptions excludeId={excludeId} onSelect={option => { onChange(option); setOpen(false) }} />}
    </Dialog>
  </div>
}
function CategoryParentOptions({ excludeId, onSelect }: { excludeId?: string; onSelect: (option: LookupOption) => void }) {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [trail, setTrail] = useState<LookupOption[]>([])
  const parent = trail.at(-1)
  const query = useCatalogList('categories', { page, pageSize: 20, search: search || undefined, rootsOnly: !search && !parent,
    parentId: search ? undefined : parent?.id, sortBy: 'name', descending: false })
  return <><LookupSearchBox label="Tìm danh mục cha" onSearch={value => { setSearch(value); setPage(1); setTrail([]) }} />
    <nav className="catalog-breadcrumb" aria-label="Nhánh danh mục cha"><Button variant="secondary" onClick={() => { setTrail([]); setPage(1); setSearch('') }}>Danh mục gốc</Button>
      {trail.map((item, index) => <Button key={item.id} variant="secondary" onClick={() => { setTrail(previous => previous.slice(0, index + 1)); setPage(1) }}>{item.name}</Button>)}
    </nav>
    {query.isFetching ? <LoadingState /> : query.error ? <CatalogError error={query.error} onRetry={() => { void query.refetch() }} /> : query.data && <>
      {!query.data.items.length && <p>Không có danh mục phù hợp.</p>}
      <ul className="catalog-tree-options">{query.data.items.map(item => <li key={item.id}>
        <Button variant="secondary" disabled={!item.isAvailable || item.id === excludeId} onClick={() => onSelect({ id: item.id, name: item.name })}>{item.name}</Button>
        {item.hasChildren && <Button variant="secondary" disabled={item.id === excludeId || trail.some(node => node.id === item.id)}
          onClick={() => { setTrail(previous => [...previous, { id: item.id, name: item.name }]); setSearch(''); setPage(1) }}>Mở nhánh {item.name}</Button>}
        {!item.isAvailable && <span className="field-hint">Không khả dụng</span>}
      </li>)}</ul>
      <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={setPage} />
    </>}
  </>
}
export function MemberCandidatePicker({ teamId, disabled, onSelect }: { teamId: string; disabled: boolean; onSelect: (candidate: TeamMemberCandidate) => void }) {
  const [open, setOpen] = useState(false)
  return <><Button disabled={disabled} onClick={() => setOpen(true)}>Thêm thành viên</Button>
    <Dialog open={open} onOpenChange={setOpen} title="Chọn người dùng hoạt động" description="Chỉ người dùng chưa tham gia nhóm. Người đã ngừng tham gia cần được kích hoạt lại từ danh sách.">
      {open && <MemberCandidateOptions teamId={teamId} onSelect={value => { onSelect(value); setOpen(false) }} />}
    </Dialog>
  </>
}
function MemberCandidateOptions({ teamId, onSelect }: { teamId: string; onSelect: (candidate: TeamMemberCandidate) => void }) {
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const query = useTeamCandidates(teamId, { page, pageSize: 20, search: search || undefined })
  return <><LookupSearchBox label="Tìm thành viên theo tên" onSearch={value => { setSearch(value); setPage(1) }} />
    {query.isFetching ? <LoadingState /> : query.error ? <CatalogError error={query.error} onRetry={() => { void query.refetch() }} /> : query.data && <>
      {!query.data.items.length && <p>Không có người dùng phù hợp.</p>}
      <ul className="account-options">{query.data.items.map(item => <li key={item.id}><Button variant="secondary" onClick={() => onSelect(item)}>
        <span>{item.displayName}<small>{item.departmentName || 'Chưa thuộc phòng ban'} · Mã {item.id.slice(-8)}</small></span>
      </Button></li>)}</ul>
      <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={setPage} />
    </>}
  </>
}
