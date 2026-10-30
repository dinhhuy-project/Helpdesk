import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { appRoutes } from '../../../app/router/routes'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import type { DashboardReport, ReportSla } from '../types/report'
import { TicketPriority, TicketStatus } from '../../tickets/types/ticket-enums'
import { reportKeys } from '../hooks/use-reports'
import { refreshRealtimeInbox } from '../../notifications/realtime/realtime-cache'

const period = { createdFrom: '2026-01-01T00:00:00.000Z', createdTo: '2026-01-03T00:00:00.000Z', asOf: '2026-10-30T00:00:00Z' }
const interval = `createdFrom=${encodeURIComponent(period.createdFrom)}&createdTo=${encodeURIComponent(period.createdTo)}`
const teamId = 'c0000000-0000-0000-0000-000000000001'
const metric = { met: 0, breached: 0, pending: 0, stopped: 0, unknown: 0, measured: 0, metPercent: null, durationSamples: 0, averageMinutes: null }
const sla: ReportSla = { withSla: 0, withoutSla: 17, excludedCancelled: 0, eligible: 0, breachedTickets: 0, firstResponse: metric, resolution: metric }
const totals = { total: 17, open: 17, resolved: 0, closed: 0, cancelled: 0, unassignedTeam: 16, unassignedAgent: 17 }
const summary: DashboardReport = { period, tickets: totals, byStatus: Object.keys(TicketStatus).map(key => ({ key, count: key === 'New' ? 17 : 0 })),
  byPriority: Object.keys(TicketPriority).map(key => ({ key, count: key === 'Normal' ? 17 : 0 })), sla }
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()) })
async function fixture(path = `/reports/tickets?${interval}`, permissions = ['ticket.read.own'], anonymous = false) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend = { profile: { ...userA, roles: [], permissions }, summary: structuredClone(summary), status: 200,
    gate: undefined as Promise<void> | undefined, profileGate: undefined as Promise<void> | undefined }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('reports', backend.profile) }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url === '/api/v1/users/me') { await backend.profileGate; return { ...response, data: backend.profile } }
    if (config.url === '/api/v1/notifications/unread-count') return { ...response, data: { count: 0 } }
    if (config.url === '/api/v1/dashboard' || config.url?.startsWith('/api/v1/reports/tickets/')) {
      await backend.gate
      if (backend.status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status: backend.status,
        headers: new AxiosHeaders({ 'Retry-After': '30' }), data: { title: 'Báo cáo bị từ chối.', traceId: 'report-trace', code: 'VersionConflict' } })
      if (config.url === '/api/v1/dashboard') return { ...response, data: structuredClone(backend.summary) }
      if (config.url.endsWith('/daily')) return { ...response, data: { period, days: [{ date: '2026-01-01T00:00:00Z', createdTickets: 17 }, { date: '2026-01-02T00:00:00Z', createdTickets: 0 }] } }
      return { ...response, data: { period, groups: { items: [{ id: null, name: 'Chưa phân công', isActive: null, tickets: { ...totals, total: 16 }, sla },
        { id: teamId, name: 'Nhóm có lịch sử', isActive: false, tickets: { ...totals, total: 1 }, sla }], page: config.params.page, pageSize: config.params.pageSize, totalCount: 42 } } }
    }
    throw new Error('Unexpected report fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter }); services.queryClient.setDefaultOptions({ queries: { retry: false } })
  if (!anonymous) await services.session.login({ email: userA.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { backend, services, router, requests, mount,
    reports: () => requests.filter(r => r.url === '/api/v1/dashboard' || r.url?.startsWith('/api/v1/reports/tickets/')) }
}
// Wait for data, rather than the heading which exists during guarded aggregate loading.
const loaded = () => screen.findByRole('table', { name: 'Chỉ số SLA' })

describe('dashboard and server aggregated ticket reports', () => {
  it.each(['/dashboard', '/reports/tickets'])('guards anonymous deep link %s before aggregate requests', async path => {
    const test = await fixture(path, [], true); test.mount()
    await screen.findByRole('heading', { name: 'Đăng nhập' }); expect(test.reports()).toHaveLength(0)
  })
  it('does not grant reporting from an admin role name or unrelated permissions', async () => {
    const test = await fixture('/dashboard', ['user.manage']); test.backend.profile.roles = []
    test.mount(); await screen.findByRole('heading', { name: 'Không có quyền truy cập' }); expect(test.reports()).toHaveLength(0)
  })
  it('waits for current profile verification before requesting or showing aggregates', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.profileGate = gate.promise
    let refresh!: Promise<unknown>; act(() => { refresh = test.services.session.refreshProfile() }); test.mount()
    expect(test.reports()).toHaveLength(0)
    await act(async () => { gate.resolve(); await refresh }); await loaded()
  })
  it('pins the server default period on the URL before group/daily calls and keeps it when switching routes', async () => {
    const test = await fixture('/dashboard'); test.mount(); await loaded()
    expect(new URLSearchParams(test.router.state.location.search).get('createdFrom')).toBe(period.createdFrom)
    const daily = test.requests.find(r => r.url?.endsWith('/daily'))!
    expect(daily.params.createdTo).toBe(period.createdTo)
    expect(test.requests.filter(r => r.url?.endsWith('/teams'))).toHaveLength(0)
    await userEvent.click(screen.getByRole('link', { name: 'Xem báo cáo cùng bộ lọc' })); await loaded()
    await screen.findByRole('table', { name: 'Theo nhóm hỗ trợ' })
    expect(test.requests.find(r => r.url?.endsWith('/teams'))?.params.createdFrom).toBe(period.createdFrom)
  })
  it.each(['ticket.read.own', 'ticket.read.team', 'ticket.read.all'])('uses API aggregates with %s and retains unassigned, inactive, zeros and null samples', async permission => {
    const test = await fixture(undefined, [permission]); test.mount(); await loaded()
    const total = screen.getByRole('list', { name: 'Theo trạng thái' }); expect(within(total).getByText('Mở lại')).toBeInTheDocument()
    expect(within(total).getAllByText('0').length).toBe(6)
    expect(screen.getByText('Chưa phân công')).toBeInTheDocument(); expect(screen.getByText('Ngừng hoạt động')).toBeInTheDocument()
    expect(screen.getAllByText('Chưa có mẫu đo').length).toBeGreaterThan(2)
    expect(screen.queryByText('0%')).not.toBeInTheDocument()
    expect(screen.getByText('Tổng Ticket', { selector: 'dt' }).nextElementSibling).toHaveTextContent('17')
    expect(test.reports().every(r => r.headers.get('Authorization') === 'Bearer access-reports')).toBe(true)
    expect(test.requests.some(r => r.url === '/api/v1/tickets' || r.url === '/api/v1/users' || r.url === '/api/v1/support-teams')).toBe(false)
  })
  it('passes the identical cohort to all aggregates and paginates groups without changing the total or client sorting', async () => {
    const test = await fixture(`/reports/tickets?${interval}&page=2&pageSize=1&status=3&priority=4&teamId=${teamId}&sortBy=open&descending=false`); test.mount(); await loaded()
    const calls = test.reports()
    expect(calls).toHaveLength(3)
    for (const request of calls) expect(request.params).toMatchObject({ createdFrom: period.createdFrom, createdTo: period.createdTo, status: 3, priority: 4, teamId })
    expect(calls.find(r => r.url?.endsWith('/teams'))?.params).toMatchObject({ page: 2, pageSize: 1, sortBy: 'open', descending: false })
    expect(screen.getByText('Trang 2 / 42 · 42 kết quả')).toBeInTheDocument()
    expect(screen.getByText('Tổng Ticket', { selector: 'dt' }).nextElementSibling).toHaveTextContent('17')
    await userEvent.click(screen.getByRole('button', { name: 'Trang sau' })); await loaded()
    expect(test.router.state.location.search).toContain('page=3')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Nhóm báo cáo' }), 'agents'); await loaded()
    await screen.findByRole('table', { name: 'Theo nhân viên xử lý' })
    expect(new URLSearchParams(test.router.state.location.search).get('page')).toBeNull()
    expect(test.reports().at(-1)?.url).toBe('/api/v1/reports/tickets/agents')
  })
  it('drills into a visible group without fetching an administrative directory and supports URL back', async () => {
    const test = await fixture(); test.mount(); await loaded()
    await userEvent.click(screen.getByRole('button', { name: 'Nhóm có lịch sử' })); await loaded()
    expect(new URLSearchParams(test.router.state.location.search).get('teamId')).toBe(teamId)
    expect(screen.getByRole('button', { name: 'Bỏ lọc nhóm' })).toBeInTheDocument()
    await act(async () => { await test.router.navigate(-1) }); await loaded()
    expect(screen.queryByRole('button', { name: 'Bỏ lọc nhóm' })).not.toBeInTheDocument()
  })
  it('applies filters as a whole and resets group page, while preserving UTC date labels', async () => {
    const test = await fixture(`/reports/tickets?${interval}&page=3`); test.mount(); await loaded()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Trạng thái' }), '6')
    expect(test.reports()).toHaveLength(3)
    await userEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' })); await loaded()
    expect(new URLSearchParams(test.router.state.location.search).get('status')).toBe('6')
    expect(new URLSearchParams(test.router.state.location.search).get('page')).toBeNull()
    await userEvent.click(screen.getByText('Xem bảng số liệu từng ngày'))
    const days = screen.getByRole('table', { name: 'Ticket tạo từng ngày UTC' })
    expect(within(days).getByText('1/1/26')).toBeInTheDocument(); expect(within(days).getByText('2/1/26')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Tạo đến'), { target: { value: '' } })
    await userEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' }))
    await screen.findByText('Cần cả thời điểm bắt đầu và kết thúc.')
    expect(test.reports()).toHaveLength(6)
  })
  it.each([403, 409, 429, 503])('hides old panels after %s and displays diagnostics without zeroing aggregates', async status => {
    const test = await fixture(); test.mount(); await loaded()
    test.backend.status = status
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: reportKeys.root(test.services.session.store.getState().scope) }) })
    await screen.findByText('Mã tra cứu: report-trace')
    expect(screen.queryByRole('table', { name: 'Chỉ số SLA' })).not.toBeInTheDocument()
    expect(screen.queryByText('Không có Ticket trong phạm vi và bộ lọc này.')).not.toBeInTheDocument()
    if (status === 429) expect(screen.getByRole('button', { name: /Tải lại số liệu/ })).toBeDisabled()
    else {
      test.backend.status = 200
      await userEvent.click(screen.getByRole('button', { name: 'Tải lại số liệu' })); await loaded()
    }
  })
  it('hides old data during refetch and discards in-flight results after a grant or user changes', async () => {
    const test = await fixture(); test.mount(); await loaded()
    const gate = deferred<void>(); test.backend.gate = gate.promise
    let pending!: Promise<unknown>
    act(() => { pending = test.services.queryClient.invalidateQueries({ queryKey: reportKeys.root(test.services.session.store.getState().scope) }) })
    await screen.findByText('Đang kiểm tra và tổng hợp báo cáo…')
    expect(screen.queryByRole('table', { name: 'Chỉ số SLA' })).not.toBeInTheDocument()
    test.backend.profile = { ...test.backend.profile, permissions: [] }
    await act(async () => { await test.services.session.refreshProfile() })
    await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    await act(async () => { gate.resolve(); await pending })
    expect(test.services.queryClient.getQueriesData({ queryKey: ['reports'] })).toHaveLength(0)
  })
  it('refetches reports on realtime reconciliation without applying event counts', async () => {
    const test = await fixture(); test.mount(); await loaded()
    const before = test.reports().length
    test.backend.summary.tickets.total = 21
    await act(async () => { await refreshRealtimeInbox(test.services.queryClient, test.services.session.store.getState().scope!, () => true) })
    await loaded(); expect(test.reports().length).toBeGreaterThan(before)
    expect(screen.getByText('Tổng Ticket', { selector: 'dt' }).nextElementSibling).toHaveTextContent('21')
  })
  it('shows an empty cohort as zero counts while retaining null SLA and every status/priority bucket', async () => {
    const test = await fixture('/dashboard?' + interval)
    test.backend.summary.tickets = { total: 0, open: 0, resolved: 0, closed: 0, cancelled: 0, unassignedTeam: 0, unassignedAgent: 0 }
    test.backend.summary.byStatus.forEach(row => { row.count = 0 }); test.backend.summary.byPriority.forEach(row => { row.count = 0 })
    test.backend.summary.sla.withoutSla = 0
    test.mount(); await loaded()
    expect(screen.getByText('Không có Ticket trong phạm vi và bộ lọc này.')).toBeInTheDocument()
    expect(within(screen.getByRole('list', { name: 'Theo trạng thái' })).getAllByText('0')).toHaveLength(7)
    expect(within(screen.getByRole('list', { name: 'Theo độ ưu tiên' })).getAllByText('0')).toHaveLength(4)
    expect(screen.getAllByText('Chưa có mẫu đo')).toHaveLength(4)
  })
  it('never restores a report when its request finishes after logout', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.gate = gate.promise; test.mount()
    await waitFor(() => expect(test.reports()).toHaveLength(3))
    await act(async () => { await test.services.session.logout() })
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    await act(async () => { gate.resolve() })
    expect(test.services.queryClient.getQueriesData({ queryKey: ['reports'] })).toHaveLength(0)
    expect(screen.queryByRole('table', { name: 'Chỉ số SLA' })).not.toBeInTheDocument()
  })
  it('does not issue API calls for invalid URL filters', async () => {
    const test = await fixture('/reports/tickets?page=0'); test.mount()
    await screen.findByText('Phân trang nhóm không hợp lệ.'); expect(test.reports()).toHaveLength(0)
  })
  it('cancels a superseded cohort request rather than displaying its late result', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.gate = gate.promise; test.mount()
    await waitFor(() => expect(test.reports()).toHaveLength(3))
    const old = test.reports()[0]!
    test.backend.gate = undefined
    await act(async () => { await test.router.navigate(`/dashboard?${interval}&status=6`) }); await loaded()
    await act(async () => { gate.resolve() })
    expect(old.signal?.aborted).toBe(true)
    expect(new URLSearchParams(test.router.state.location.search).get('status')).toBe('6')
  })
})
