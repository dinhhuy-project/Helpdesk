import { Link, useSearchParams } from 'react-router-dom'
import { PageTitle } from '../../../shared/components/page-title'
import { Button } from '../../../shared/components/button'
import { DataTable } from '../../../shared/components/data-table'
import { Pagination } from '../../../shared/components/pagination'
import { FormField, Select } from '../../../shared/components/form-field'
import { getTicketPriorityLabel } from '../../tickets/types/ticket-enums'
import { useSlaPolicies } from '../hooks/use-sla'
import { parseSlaPage } from '../lib/sla-policy-form'
import { SlaError } from '../components/sla-state'

export function SlaPolicyListPage() {
  const [params, setParams] = useSearchParams()
  const parsed = parseSlaPage(params)
  const search = parsed.value ?? { page: 1, pageSize: 20 }
  const query = useSlaPolicies(search, parsed.value !== undefined)
  const change = (page: number, pageSize: number) => {
    const next = new URLSearchParams(); if (page !== 1) next.set('page', String(page)); if (pageSize !== 20) next.set('pageSize', String(pageSize)); setParams(next)
  }
  return <section className="account-page"><PageTitle title="Chính sách SLA" /><p className="eyebrow">Quản trị</p><h1>Chính sách SLA</h1>
    <p>Policy tính 24/7 cho Ticket mới. Cả policy hoạt động và lịch sử ngừng hoạt động đều được giữ.</p>
    <p><Link className="button button--primary" to="/admin/sla-policies/new">Tạo policy SLA</Link></p>
    {parsed.error ? <div role="alert"><p>{parsed.error}</p><Button variant="secondary" onClick={() => setParams({})}>Về trang đầu</Button></div> : query.isError ?
      <SlaError error={query.error} onRetry={() => { void query.refetch() }} /> : <>
        <FormField label="Policy mỗi trang">{field => <Select {...field} value={search.pageSize} onChange={event => change(1, Number(event.target.value))}>
          {![10, 20, 50, 100].includes(search.pageSize) && <option value={search.pageSize}>{search.pageSize}</option>}
          {[10, 20, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}
        </Select>}</FormField>
        <DataTable caption="Danh sách policy SLA" rows={query.data?.items ?? []} rowKey={row => row.id} loading={query.isPending || query.isFetching}
          columns={[
            { key: 'name', header: 'Policy', render: row => <Link to={`/admin/sla-policies/${row.id}`}>{row.name}</Link> },
            { key: 'priority', header: 'Ưu tiên', render: row => getTicketPriorityLabel(row.priority) },
            { key: 'response', header: 'Phản hồi (phút)', render: row => row.firstResponseMinutes.toLocaleString('vi-VN') },
            { key: 'resolution', header: 'Giải quyết (phút)', render: row => row.resolutionMinutes.toLocaleString('vi-VN') },
            { key: 'active', header: 'Trạng thái', render: row => row.isActive ? 'Đang hoạt động' : 'Đã ngừng hoạt động' },
          ]} />
        {query.data && !query.isFetching && <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={page => change(page, search.pageSize)} />}
      </>}
  </section>
}
