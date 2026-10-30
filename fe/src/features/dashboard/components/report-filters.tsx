import { useState } from 'react'
import { FormField, Input, Select } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { ticketPriorityLabels, ticketStatusLabels } from '../../tickets/types/ticket-enums'
import { localDateTime, parseReportSearch, reportSearchParams } from '../lib/report-search'
import type { ReportSearch } from '../types/report'

export function ReportFilters({ search, change }: { search: ReportSearch; change: (search: ReportSearch) => void }) {
  const [from, setFrom] = useState(localDateTime(search.createdFrom))
  const [to, setTo] = useState(localDateTime(search.createdTo))
  const [status, setStatus] = useState(String(search.status ?? ''))
  const [priority, setPriority] = useState(String(search.priority ?? ''))
  const [error, setError] = useState('')
  return <form className="report-filters" onSubmit={event => {
    event.preventDefault()
    try {
      const params = reportSearchParams({ ...search, page: 1 })
      for (const [key, value] of [['createdFrom', from], ['createdTo', to]] as const) {
        // Preserve the original instant when only another field changes, including DST's repeated local hour.
        if (value) params.set(key, value === localDateTime(search[key]) && search[key] ? search[key] : new Date(value).toISOString())
        else params.delete(key)
      }
      if (status) params.set('status', status); else params.delete('status')
      if (priority) params.set('priority', priority); else params.delete('priority')
      const result = parseReportSearch(params)
      if (result.error) { setError(result.error); return }
      if (result.value) { setError(''); change(result.value) }
    } catch { setError('Thời gian không hợp lệ.') }
  }}>
    <p className="report-filters__hint field-hint">Thời gian nhập theo {Intl.DateTimeFormat().resolvedOptions().timeZone}. Khoảng tạo Ticket: từ (bao gồm) đến (không bao gồm), tối đa 366 ngày.</p>
    <FormField label="Tạo từ">{field => <Input {...field} type="datetime-local" step="0.001" value={from} onChange={event => setFrom(event.target.value)} />}</FormField>
    <FormField label="Tạo đến">{field => <Input {...field} type="datetime-local" step="0.001" value={to} onChange={event => setTo(event.target.value)} />}</FormField>
    <FormField label="Trạng thái">{field => <Select {...field} value={status} onChange={event => setStatus(event.target.value)}><option value="">Tất cả</option>
      {Object.entries(ticketStatusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select>}</FormField>
    <FormField label="Độ ưu tiên">{field => <Select {...field} value={priority} onChange={event => setPriority(event.target.value)}><option value="">Tất cả</option>
      {Object.entries(ticketPriorityLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select>}</FormField>
    <div className="report-filter-actions"><Button type="submit">Áp dụng bộ lọc</Button><Button type="button" variant="secondary" onClick={() => change({ view: search.view, page: 1, pageSize: search.pageSize, sortBy: search.sortBy, descending: search.descending })}>30 ngày mặc định</Button></div>
    {search.teamId && <p className="report-filter-chip">Đang lọc theo nhóm đã chọn. <Button type="button" variant="secondary" onClick={() => change({ ...search, page: 1, teamId: undefined })}>Bỏ lọc nhóm</Button></p>}
    {search.agentId && <p className="report-filter-chip">Đang lọc theo nhân viên đã chọn. <Button type="button" variant="secondary" onClick={() => change({ ...search, page: 1, agentId: undefined })}>Bỏ lọc nhân viên</Button></p>}
    {error && <p className="field-error report-filters__hint" role="alert">{error}</p>}
  </form>
}
