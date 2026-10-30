import { useState } from 'react'
import { FormField, Input, Select } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import type { AccountSearch } from '../types/account'

export function AccountFilters({ value, onApply }: { value: AccountSearch; onApply: (value: AccountSearch) => void }) {
  const [search, setSearch] = useState(value.search ?? '')
  const [active, setActive] = useState(value.isActive === undefined ? '' : String(value.isActive))
  const [sort, setSort] = useState(value.sortBy)
  const [descending, setDescending] = useState(String(value.descending))
  const [size, setSize] = useState(value.pageSize)
  return <form className="account-filters panel" onSubmit={event => {
    event.preventDefault()
    onApply({ page: 1, pageSize: size, search: search.trim() || undefined, isActive: active ? active === 'true' : undefined,
      sortBy: sort, descending: descending === 'true' })
  }}>
    <FormField label="Tìm theo tên hoặc email">{props => <Input {...props} value={search} maxLength={200} onChange={event => setSearch(event.target.value)} type="search" />}</FormField>
    <FormField label="Trạng thái tài khoản">{props => <Select {...props} value={active} onChange={event => setActive(event.target.value)}>
      <option value="">Tất cả</option><option value="true">Đang hoạt động</option><option value="false">Đã vô hiệu hóa</option>
    </Select>}</FormField>
    <FormField label="Sắp xếp theo">{props => <Select {...props} value={sort} onChange={event => {
      const next = event.target.value
      if (next === 'createdAt' || next === 'email' || next === 'displayName') setSort(next)
    }}><option value="createdAt">Ngày tạo</option><option value="email">Email</option><option value="displayName">Tên hiển thị</option></Select>}</FormField>
    <FormField label="Thứ tự">{props => <Select {...props} value={descending} onChange={event => setDescending(event.target.value)}>
      <option value="true">Giảm dần</option><option value="false">Tăng dần</option></Select>}</FormField>
    <FormField label="Kết quả mỗi trang">{props => <Select {...props} value={size} onChange={event => setSize(Number(event.target.value))}>
      {[...new Set([10, 20, 50, 100, value.pageSize])].sort((a,b) => a-b).map(n => <option key={n} value={n}>{n}</option>)}
    </Select>}</FormField>
    <Button type="submit">Áp dụng bộ lọc</Button>
    <Button variant="secondary" onClick={() => onApply({ page: 1, pageSize: 20, sortBy: 'createdAt', descending: true })}>Xóa bộ lọc</Button>
  </form>
}
