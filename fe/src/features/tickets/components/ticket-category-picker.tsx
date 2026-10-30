import { useId, useState } from 'react'
import { Button } from '../../../shared/components/button'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { Input } from '../../../shared/components/form-field'
import { Pagination } from '../../../shared/components/pagination'
import { useTicketCategoryLookup } from '../hooks/use-ticket-category-lookup'
import type { SelectedTicketCategory, TicketCategoryLookup } from '../types/ticket-category'

interface CategoryCursor { path: TicketCategoryLookup[]; page: number; search: string }
export function TicketCategoryPicker({ value, onChange, error, disabled = false }: {
  value: SelectedTicketCategory | null
  onChange: (value: SelectedTicketCategory) => void
  error?: string
  disabled?: boolean
}) {
  const id = useId()
  const [cursor, setCursor] = useState<CategoryCursor>({ path: [], page: 1, search: '' })
  const [search, setSearch] = useState('')
  const parent = cursor.path.at(-1)
  const query = useTicketCategoryLookup({ page: cursor.page, pageSize: 20, search: cursor.search || undefined,
    rootsOnly: !parent, parentId: parent?.id })
  function openPath(path: TicketCategoryLookup[]) {
    setCursor({ path, page: 1, search: '' })
    setSearch('')
  }
  function applySearch() {
    setCursor({ ...cursor, page: 1, search: search.trim() })
  }
  return <fieldset className="ticket-category-picker" disabled={disabled} aria-describedby={`${id}-hint${error ? ` ${id}-error` : ''}`}>
    <legend>Danh mục yêu cầu <span aria-hidden="true">*</span></legend>
    <p id={`${id}-hint`} className="field-hint">Chọn danh mục phù hợp. Có thể chọn cả danh mục cha hoặc mở để chọn danh mục con.</p>
    <nav className="category-breadcrumbs" aria-label="Đường dẫn danh mục">
      <Button variant="secondary" onClick={() => openPath([])} aria-current={!parent ? 'location' : undefined}>Danh mục gốc</Button>
      {cursor.path.map((node, index) => <Button key={node.id} variant="secondary"
        aria-current={index === cursor.path.length - 1 ? 'location' : undefined}
        onClick={() => openPath(cursor.path.slice(0, index + 1))}>{node.name}</Button>)}
    </nav>
    <div className="category-search"><label className="sr-only" htmlFor={`${id}-search`}>Tìm danh mục trong cấp đang mở</label>
      <Input id={`${id}-search`} type="search" value={search} maxLength={200} placeholder="Tìm trong cấp đang mở…"
        onChange={event => setSearch(event.target.value)} onKeyDown={event => {
          if (event.key === 'Enter') { event.preventDefault(); applySearch() }
        }} />
      <Button variant="secondary" onClick={applySearch}>Tìm danh mục</Button>
    </div>
    <div className="category-results" aria-busy={query.isFetching}>
      {query.isPending || query.isFetching ? <LoadingState label="Đang tải danh mục…" />
        : query.isError ? <ErrorState error={query.error} onRetry={() => { void query.refetch() }} />
          : query.data.items.length ? <ul className="category-options">{query.data.items.map(category => <li key={category.id}>
            <label><input type="radio" name="ticket-category-choice" value={category.id} checked={value?.category.id === category.id}
              aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined}
              onChange={() => onChange({ category, path: [...cursor.path.map(node => node.name), category.name] })} />
              <span>{category.name}</span></label>
            {category.hasChildren && <Button variant="secondary" aria-label={`Mở danh mục ${category.name}`}
              onClick={() => openPath([...cursor.path, category])}>Mở nhánh →</Button>}
          </li>)}</ul> : <EmptyState title="Không có danh mục phù hợp" description="Thử tìm kiếm khác hoặc quay về cấp trước." />}
      {query.data && !query.isFetching && !query.isError && <Pagination page={query.data.page} pageSize={query.data.pageSize}
        totalCount={query.data.totalCount} disabled={disabled} onPageChange={page => setCursor({ ...cursor, page })} />}
    </div>
    <p className="category-selection" role="status">{value ? `Đã chọn: ${value.path.join(' / ')}` : 'Chưa chọn danh mục.'}</p>
    {error && <p id={`${id}-error`} className="field-error" role="alert">{error}</p>}
  </fieldset>
}
