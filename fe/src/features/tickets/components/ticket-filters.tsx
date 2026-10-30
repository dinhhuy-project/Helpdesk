import { useState } from 'react'
import type { FormEvent } from 'react'
import { useAuth } from '../../auth/hooks/use-auth'
import { Button } from '../../../shared/components/button'
import { FormField, Input, Select } from '../../../shared/components/form-field'
import { parseTicketSearch, ticketSearchParams } from '../lib/ticket-search'
import type { TicketSearch } from '../lib/ticket-search'
import { ticketPriorityLabels, ticketStatusLabels } from '../types/ticket-enums'

function localTime(value?: string) {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, -1)
}

export function TicketFilters({ value, onChange }: { value: TicketSearch; onChange: (value: TicketSearch) => void }) {
  const { user } = useAuth()
  const [error, setError] = useState<string>()
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const params = ticketSearchParams(value)
    params.set('page', '1')
    for (const key of ['search', 'ticketNumber', 'status', 'priority', 'unassigned', 'sortBy', 'descending', 'pageSize', 'requesterId', 'agentId']) {
      const raw = String(data.get(key) ?? '').trim()
      if (raw) params.set(key, raw)
      else params.delete(key)
    }
    for (const key of ['createdFrom', 'createdTo']) {
      const raw = String(data.get(key) ?? '')
      if (raw) {
        const date = new Date(raw)
        if (!Number.isFinite(date.getTime())) { setError('Thời gian lọc không hợp lệ.'); return }
        params.set(key, date.toISOString())
      } else params.delete(key)
    }
    const result = parseTicketSearch(params)
    if (result.error !== undefined) setError(result.error)
    else { setError(undefined); onChange(result.value) }
  }
  return <form className="ticket-filters" aria-label="Bộ lọc yêu cầu" onSubmit={submit}>
    <div className="ticket-filter-grid">
      <FormField label="Tìm trong tiêu đề">{props => <Input {...props} name="search" maxLength={200}
        placeholder="Nhập tiêu đề yêu cầu…" defaultValue={value.search} />}</FormField>
      <FormField label="Số yêu cầu" hint="Chỉ nhập số, ví dụ 42.">{props => <Input {...props} name="ticketNumber"
        inputMode="numeric" maxLength={19} defaultValue={value.ticketNumber} />}</FormField>
      <FormField label="Trạng thái">{props => <Select {...props} name="status" defaultValue={value.status ?? ''}>
        <option value="">Tất cả trạng thái</option>{Object.entries(ticketStatusLabels).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
      </Select>}</FormField>
      <FormField label="Độ ưu tiên">{props => <Select {...props} name="priority" defaultValue={value.priority ?? ''}>
        <option value="">Tất cả mức ưu tiên</option>{Object.entries(ticketPriorityLabels).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
      </Select>}</FormField>
    </div>
    <details className="ticket-more-filters"><summary>Lọc và sắp xếp thêm</summary>
      <div className="ticket-filter-grid">
        <FormField label="Người yêu cầu">{props => <Select {...props} name="requesterId" defaultValue={value.requesterId ?? ''}>
          <option value="">Bất kỳ người yêu cầu</option><option value={user?.id}>Yêu cầu của tôi</option>
          {value.requesterId && value.requesterId !== user?.id && <option value={value.requesterId}>Người yêu cầu từ liên kết</option>}
        </Select>}</FormField>
        <FormField label="Người xử lý">{props => <Select {...props} name="agentId" defaultValue={value.agentId ?? ''}>
          <option value="">Bất kỳ người xử lý</option><option value={user?.id}>Được giao cho tôi</option>
          {value.agentId && value.agentId !== user?.id && <option value={value.agentId}>Người xử lý từ liên kết</option>}
        </Select>}</FormField>
        <FormField label="Phân công">{props => <Select {...props} name="unassigned" defaultValue={value.unassigned === undefined ? '' : String(value.unassigned)}>
          <option value="">Tất cả</option><option value="true">Chưa có người xử lý</option><option value="false">Đã có người xử lý</option>
        </Select>}</FormField>
        <FormField label="Sắp xếp theo">{props => <Select {...props} name="sortBy" defaultValue={value.sortBy}>
          <option value="createdAt">Ngày tạo</option><option value="updatedAt">Ngày cập nhật</option>
          <option value="ticketNumber">Số yêu cầu</option><option value="priority">Độ ưu tiên</option>
        </Select>}</FormField>
        <FormField label="Thứ tự">{props => <Select {...props} name="descending" defaultValue={String(value.descending)}>
          <option value="true">Giảm dần</option><option value="false">Tăng dần</option>
        </Select>}</FormField>
        <FormField label="Kết quả mỗi trang">{props => <Select {...props} name="pageSize" defaultValue={value.pageSize}>
          {[...new Set([10, 20, 50, 100, value.pageSize])].sort((a, b) => a - b).map(size => <option key={size} value={size}>{size}</option>)}
        </Select>}</FormField>
        <FormField label="Tạo từ thời điểm" hint="Theo giờ trên thiết bị.">{props => <Input {...props} type="datetime-local" step="0.001" name="createdFrom" defaultValue={localTime(value.createdFrom)} />}</FormField>
        <FormField label="Tạo trước thời điểm" hint="Không gồm mốc kết thúc.">{props => <Input {...props} type="datetime-local" step="0.001" name="createdTo" defaultValue={localTime(value.createdTo)} />}</FormField>
      </div>
    </details>
    {(value.teamId || value.categoryId) && <p className="field-hint">Đang lọc theo {value.teamId ? 'nhóm hỗ trợ' : ''}{value.teamId && value.categoryId ? ' và ' : ''}{value.categoryId ? 'danh mục' : ''} từ liên kết. Xóa bộ lọc để bỏ giới hạn này.</p>}
    {error && <p role="alert" className="field-error">{error}</p>}
    <div className="ticket-filter-actions"><Button type="submit">Áp dụng bộ lọc</Button>
      <Button variant="secondary" onClick={() => onChange({ page: 1, pageSize: 20, sortBy: 'createdAt', descending: true })}>Xóa bộ lọc</Button>
    </div>
  </form>
}
