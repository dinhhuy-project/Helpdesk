import { useState } from 'react'
import { FormField, Input, Select } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { TeamDepartmentPicker } from './catalog-lookups'
import type { CatalogKind, CatalogSearch, LookupOption } from '../types/catalog'

export function CatalogFilters({ kind, value, onApply }: { kind: CatalogKind; value: CatalogSearch; onApply: (value: CatalogSearch) => void }) {
  const [search, setSearch] = useState(value.search ?? '')
  const [status, setStatus] = useState(value.isActive === undefined ? '' : String(value.isActive))
  const [sort, setSort] = useState(value.sortBy)
  const [descending, setDescending] = useState(String(value.descending))
  const [size, setSize] = useState(value.pageSize)
  const [roots, setRoots] = useState(value.rootsOnly ?? false)
  const [department, setDepartment] = useState<LookupOption | null>(value.departmentId ? { id: value.departmentId, name: 'Phòng ban đang lọc' } : null)
  return <form className="account-filters panel" onSubmit={event => {
    event.preventDefault()
    onApply({ ...value, page: 1, pageSize: size, search: search.trim() || undefined, isActive: status ? status === 'true' : undefined, sortBy: sort,
      descending: descending === 'true', ...(kind === 'categories' ? { rootsOnly: roots, parentId: roots ? undefined : value.parentId } : {}),
      ...(kind === 'teams' ? { departmentId: department?.id } : {}) })
  }}>
    <FormField label="Tìm theo tên">{props => <Input {...props} type="search" value={search} maxLength={200} onChange={event => setSearch(event.target.value)} />}</FormField>
    <FormField label="Trạng thái">{props => <Select {...props} value={status} onChange={event => setStatus(event.target.value)}><option value="">Tất cả</option><option value="true">Đang hoạt động</option><option value="false">Đã vô hiệu hóa</option></Select>}</FormField>
    <FormField label="Sắp xếp theo">{props => <Select {...props} value={sort} onChange={event => { if (event.target.value === 'name' || event.target.value === 'createdAt') setSort(event.target.value) }}><option value="name">Tên</option><option value="createdAt">Ngày tạo</option></Select>}</FormField>
    <FormField label="Thứ tự">{props => <Select {...props} value={descending} onChange={event => setDescending(event.target.value)}><option value="false">Tăng dần</option><option value="true">Giảm dần</option></Select>}</FormField>
    <FormField label="Kết quả mỗi trang">{props => <Select {...props} value={size} onChange={event => setSize(Number(event.target.value))}>{[...new Set([10,20,50,100,value.pageSize])].sort((a,b) => a-b).map(n => <option key={n} value={n}>{n}</option>)}</Select>}</FormField>
    {kind === 'categories' && <label className="catalog-checkbox"><input type="checkbox" checked={roots} onChange={event => setRoots(event.target.checked)} />Chỉ danh mục gốc</label>}
    {kind === 'teams' && <div><TeamDepartmentPicker value={department} disabled={false} onChange={setDepartment} />{department && <Button variant="secondary" onClick={() => setDepartment(null)}>Bỏ lọc phòng ban</Button>}</div>}
    <Button type="submit">Áp dụng bộ lọc</Button><Button variant="secondary" onClick={() => onApply({ page: 1, pageSize: 20, sortBy: 'name', descending: false, ...(kind === 'categories' ? { rootsOnly: true } : {}) })}>Xóa bộ lọc</Button>
  </form>
}
