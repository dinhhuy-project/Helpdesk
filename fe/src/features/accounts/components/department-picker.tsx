import { useState } from 'react'
import { useDepartments } from '../hooks/use-accounts'
import { AccountError } from './account-state'
import { FormField, Input } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { Pagination } from '../../../shared/components/pagination'
import { LoadingState } from '../../../shared/components/async-state'
import { Dialog } from '../../../shared/components/dialog'

export function DepartmentPicker({ value, originalId, originalName, onChange, disabled }: {
  value: string | null; originalId: string | null; originalName: string | null; onChange: (id: string | null) => void; disabled: boolean
}) {
  const [open, setOpen] = useState(false)
  const [label, setLabel] = useState('')
  return <div className="form-field"><span className="account-field-label">Phòng ban</span>
    <p>{value ? (label || (value === originalId && originalName) || 'Phòng ban đã liên kết') : 'Chưa thuộc phòng ban'}</p>
    {originalId && value === originalId && <p className="field-hint">Giữ nguyên liên kết lịch sử kể cả khi phòng ban không còn hoạt động.</p>}
    <div className="account-actions"><Button variant="secondary" disabled={disabled} onClick={() => setOpen(true)}>Chọn phòng ban</Button>
      <Button variant="secondary" disabled={disabled || !value} onClick={() => { onChange(null); setLabel('') }}>Bỏ phòng ban</Button></div>
    <Dialog title="Chọn phòng ban đang hoạt động" open={open} onOpenChange={setOpen}>
      {open && <DepartmentOptions onSelect={(id, name) => { onChange(id); setLabel(name); setOpen(false) }} />}
    </Dialog>
  </div>
}
function DepartmentOptions({ onSelect }: { onSelect: (id: string, name: string) => void }) {
  const [draft, setDraft] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const query = useDepartments(page, search)
  const apply = () => { setSearch(draft.trim()); setPage(1) }
  // This dialog belongs to the profile form. Keep lookup search from submitting that form.
  return <><div className="account-actions" role="search" aria-label="Tìm phòng ban">
    <FormField label="Tìm phòng ban">{props => <Input {...props} value={draft} maxLength={200} onChange={event => setDraft(event.target.value)}
      onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); apply() } }} />}</FormField>
    <Button onClick={apply}>Tìm phòng ban</Button></div>
    {query.isFetching ? <LoadingState /> : query.error ? <AccountError error={query.error} onRetry={() => { void query.refetch() }} /> : query.data && <>
      {!query.data.items.length && <p>Không có phòng ban phù hợp.</p>}
      <ul className="account-options">{query.data.items.map(item => <li key={item.id}><Button variant="secondary" onClick={() => onSelect(item.id, item.name)}>{item.name}</Button></li>)}</ul>
      <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={setPage} />
    </>}
  </>
}
