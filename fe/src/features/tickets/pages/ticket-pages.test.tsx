import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { appRoutes } from '../../../app/router/routes'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { Permission } from '../../auth/lib/permissions'
import type { UserProfile } from '../../auth/types/auth'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import type { TicketAccess, TicketActivity, TicketDetails, TicketSummary } from '../types/ticket'
import type { PagedResult } from '../../../shared/api/pagination'
import { noAttachmentUpload } from '../../../test/attachment-fixtures'
import { noSlaForPath } from '../../../test/sla-fixtures'

const ticketId = '10000000-0000-0000-0000-000000000001'
const otherId = '10000000-0000-0000-0000-000000000002'
const ticket: TicketDetails = {
  id: ticketId, ticketNumber: 42, title: 'Không kết nối VPN', description: 'Nội dung riêng của Ticket',
  status: 2, priority: 3, requesterId: userA.id, categoryId: otherId, assignedTeamId: otherId,
  assignedAgentId: null, createdAt: '2026-10-29T00:00:00Z', updatedAt: null, version: 81,
  resolutionSummary: null, resolvedAt: null, closedAt: null,
}
const page = <T,>(items: T[], pageNumber = 1, totalCount = items.length): PagedResult<T> => ({ items, page: pageNumber, pageSize: 20, totalCount })
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); for (const dispose of disposals.splice(0)) dispose() })

async function openTickets(path = '/tickets', profile: UserProfile = userA) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: {
    profile: UserProfile
    list: PagedResult<TicketSummary>
    detail: TicketDetails
    activities: PagedResult<TicketActivity>
    access: TicketAccess
    accessStatus: number
    detailStatus: number
    activityStatus: number
    listStatus: number
    accessGate?: Promise<TicketAccess>
    detailGate?: Promise<TicketDetails>
    listGate?: Promise<PagedResult<TicketSummary>>
  } = { profile, list: page([ticket], 1, 45), detail: ticket, activities: page([]),
    access: { canRead: true, canAssign: false, canUpdateStatus: false },
    accessStatus: 200, detailStatus: 200, activityStatus: 200, listStatus: 200 }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined,
        { ...response, status, headers: new AxiosHeaders({ 'Retry-After': '30' }), data: { title: 'Rejected', traceId: 'ticket-trace' } })
    }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('tickets', backend.profile) }
    if (config.url === '/api/v1/users/me') return { ...response, data: backend.profile }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.endsWith('/sla')) return { ...response, data: noSlaForPath(config.url) }
    if (config.url?.endsWith('/attachments/options')) return { ...response, data: noAttachmentUpload }
    if (config.url?.endsWith('/attachments')) return { ...response, data: page([]) }
    if (config.url?.endsWith('/workflow')) return { ...response, data: { ...backend.detail, canAssign: false, assignmentIsValid: false, transitions: [] } }
    if (config.url?.endsWith('/discussion-access')) return { ...response, data: { canReadInternal: false, canCommentPublic: false, canCommentInternal: false } }
    if (config.url?.endsWith('/comments')) return { ...response, data: page([]) }
    if (config.url === '/api/v1/tickets') { fail(backend.listStatus); return { ...response, data: backend.listGate ? await backend.listGate : backend.list } }
    if (config.url?.endsWith('/access')) { fail(backend.accessStatus); return { ...response, data: backend.accessGate ? await backend.accessGate : backend.access } }
    if (config.url?.endsWith('/activities')) { fail(backend.activityStatus); return { ...response, data: backend.activities } }
    if (config.url?.startsWith('/api/v1/tickets/')) { fail(backend.detailStatus); return { ...response, data: backend.detailGate ? await backend.detailGate : backend.detail } }
    throw new Error('Unexpected fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: profile.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  // Let each case arrange its server fixture before mounting the page.
  const mount = () => render(<QueryClientProvider client={services.queryClient}>
    <AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider>
  </QueryClientProvider>)
  return { services, router, backend, requests, mount }
}

describe('Ticket list routing, server queries and permission UX', () => {
  it.each([Permission.TicketReadOwn, Permission.TicketReadTeam, Permission.TicketReadAll])('renders only server rows for %s without adding an assignee/role predicate', async permission => {
    const test = await openTickets('/tickets?status=2&page=3&pageSize=20&sortBy=priority&descending=false',
      { ...userA, roles: ['CustomRole'], permissions: [permission] })
    test.backend.list = page([{ ...ticket, requesterId: otherId, assignedAgentId: otherId }], 3, 45)
    test.mount()
    expect(await screen.findByRole('link', { name: /HD-000042/ })).toBeInTheDocument()
    const config = test.requests.find(item => item.url === '/api/v1/tickets')
    expect(config?.params).toMatchObject({ page: 3, pageSize: 20, status: 2, sortBy: 'priority', descending: false })
    expect(config?.params.requesterId).toBeUndefined()
    expect(config?.params.agentId).toBeUndefined()
    expect(config?.headers.get('Authorization')).toBe('Bearer access-tickets')
    expect(config?.signal).toBeInstanceOf(AbortSignal)
    expect(screen.getByText('Trang 3 / 3 · 45 kết quả')).toBeInTheDocument()
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
  })

  it('sends filter/sort/page size to the server, resets page and restores the URL on back', async () => {
    const test = await openTickets('/tickets?search=VPN&status=2&page=3')
    test.mount()
    await screen.findByRole('link', { name: /HD-000042/ })
    await userEvent.selectOptions(screen.getByLabelText('Độ ưu tiên'), '4')
    await userEvent.selectOptions(screen.getByLabelText('Kết quả mỗi trang'), '50')
    await userEvent.selectOptions(screen.getByLabelText('Sắp xếp theo'), 'priority')
    await userEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' }))
    await waitFor(() => expect(test.requests.filter(item => item.url === '/api/v1/tickets').at(-1)?.params)
      .toMatchObject({ page: 1, pageSize: 50, priority: 4, sortBy: 'priority', status: 2, search: 'VPN' }))
    expect(test.router.state.location.search).not.toContain('page=3')
    await act(async () => { await test.router.navigate(-1) })
    expect(screen.getByLabelText('Độ ưu tiên')).toHaveValue('')
    expect(screen.getByLabelText('Trạng thái')).toHaveValue('2')
    expect(test.router.state.location.search).toContain('page=3')
  })

  it('uses server pagination and keeps an empty out-of-range page with its real count', async () => {
    const test = await openTickets('/tickets?page=5')
    test.backend.list = page([], 5, 23)
    test.mount()
    expect(await screen.findByText('Trang 5 / 2 · 23 kết quả')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Trang sau' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Trang trước' }))
    await waitFor(() => expect(test.router.state.location.search).toContain('page=2'))
    expect(test.requests.filter(item => item.url === '/api/v1/tickets').at(-1)?.params.page).toBe(2)
  })

  it('does not fetch on an invalid filter or infer read permission from SystemAdmin/assignee actions', async () => {
    const test = await openTickets('/tickets?page=-1')
    test.mount()
    expect(screen.getByRole('heading', { name: 'Bộ lọc chưa hợp lệ' })).toBeInTheDocument()
    expect(test.requests.some(item => item.url?.startsWith('/api/v1/tickets'))).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Xóa bộ lọc' }))
    await screen.findByRole('link', { name: /HD-000042/ })
    test.backend.profile = { ...userA, roles: ['SystemAdmin', 'SupportAgent'], permissions: [Permission.TicketAssign, Permission.TicketUpdateStatus] }
    await act(async () => { await test.services.session.refreshProfile() })
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /HD-000042/ })).not.toBeInTheDocument()
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
  })

  it('hides previous rows during filter changes even if a transport ignores cancellation', async () => {
    const test = await openTickets()
    test.mount()
    await screen.findByRole('link', { name: /HD-000042/ })
    const gate = deferred<PagedResult<TicketSummary>>()
    test.backend.listGate = gate.promise
    await act(async () => { await test.router.navigate('/tickets?status=4') })
    expect(screen.queryByRole('link', { name: /HD-000042/ })).not.toBeInTheDocument()
    test.backend.listGate = undefined
    test.backend.list = page([])
    await act(async () => { await test.router.navigate('/tickets?status=5') })
    await screen.findByText('Chưa có dữ liệu')
    await act(async () => { gate.resolve(page([ticket])); await gate.promise })
    expect(screen.queryByRole('link', { name: /HD-000042/ })).not.toBeInTheDocument()
  })

  it.each([403, 429])('handles list HTTP %i without trying to refresh tokens', async status => {
    const test = await openTickets()
    test.backend.listStatus = status
    test.mount()
    if (status === 403) await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    else expect(await screen.findByRole('alert')).toHaveTextContent('30 giây')
    expect(test.requests.some(item => item.url === '/api/v1/auth/refresh')).toBe(false)
    expect(screen.queryByRole('link', { name: /HD-000042/ })).not.toBeInTheDocument()
  })
})

describe('Ticket direct links, access boundary and separate activity contract', () => {
  it('checks access before loading detail and activities even when actor is the assignee', async () => {
    const test = await openTickets(`/tickets/${ticketId}`)
    const gate = deferred<TicketAccess>()
    test.backend.accessGate = gate.promise
    test.backend.detail = { ...ticket, assignedAgentId: userA.id }
    test.mount()
    expect(screen.getByRole('status')).toHaveTextContent('Đang kiểm tra quyền')
    expect(test.requests.some(item => item.url === `/api/v1/tickets/${ticketId}`)).toBe(false)
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
    await act(async () => { gate.resolve(test.backend.access); await gate.promise })
    expect(await screen.findByText(ticket.description)).toBeInTheDocument()
    await screen.findByText('Chưa có hoạt động')
    const paths = test.requests.map(item => item.url)
    expect(paths.indexOf(`/api/v1/tickets/${ticketId}/access`)).toBeLessThan(paths.indexOf(`/api/v1/tickets/${ticketId}`))
    expect(paths.indexOf(`/api/v1/tickets/${ticketId}`)).toBeLessThan(paths.indexOf(`/api/v1/tickets/${ticketId}/activities`))
    expect(screen.getByRole('link', { name: '← Danh sách yêu cầu' })).toHaveAttribute('href', '/tickets')
    expect(screen.queryByRole('button', { name: /Phân công|Chuyển trạng thái/ })).not.toBeInTheDocument()
  })

  it.each([403, 404])('rejects an out-of-scope direct link with HTTP %i without reading detail/activity', async status => {
    const test = await openTickets(`/tickets/${ticketId}`)
    test.backend.accessStatus = status
    test.mount()
    await screen.findByRole('heading', { name: status === 403 ? 'Không có quyền truy cập' : 'Không tìm thấy yêu cầu' })
    expect(test.requests.some(item => item.url === `/api/v1/tickets/${ticketId}` || item.url?.endsWith('/activities'))).toBe(false)
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
  })

  it('fails closed for a false canRead and malformed GUID without an extra API request', async () => {
    const test = await openTickets(`/tickets/${ticketId}`)
    test.backend.access.canRead = false
    test.mount()
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    const requestsBefore = test.requests.length
    await act(async () => { await test.router.navigate('/tickets/invalid') })
    expect(screen.getByRole('heading', { name: 'Không tìm thấy yêu cầu' })).toBeInTheDocument()
    expect(test.requests.length).toBe(requestsBefore)
  })

  it('handles detail losing scope after access passed', async () => {
    const test = await openTickets(`/tickets/${ticketId}`)
    test.backend.detailStatus = 404
    test.mount()
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(test.requests.some(item => item.url?.endsWith('/activities'))).toBe(false)
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
  })

  it('removes detail, activity and cache on access revalidation/revocation', async () => {
    const test = await openTickets(`/tickets/${ticketId}`)
    test.mount()
    await screen.findByText(ticket.description)
    await screen.findByText('Chưa có hoạt động')
    test.backend.accessStatus = 404
    await act(async () => { await test.services.queryClient.invalidateQueries({ predicate: query => query.queryKey.at(-1) === 'access' }) })
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
    await waitFor(() => expect(test.services.queryClient.getQueryCache().getAll().some(query =>
      query.queryKey.includes('detail') || query.queryKey.includes('activities'))).toBe(false))
  })

  it('keeps activity paging separate, shows only projected fields and renders text safely', async () => {
    const test = await openTickets(`/tickets/${ticketId}?activityPage=2`)
    test.backend.detail = { ...ticket, description: '<img src=x onerror=alert(1)>' }
    test.backend.activities = page([{ id: otherId, ticketId, actorId: userA.id, action: 'TicketStatusChanged', createdAt: ticket.createdAt,
      oldValue: { status: 'New' }, newValue: { status: 'InProgress', resolutionSummary: '<script>secret</script>',
        privateFutureField: 'must-not-render', content: 'private-comment-content' } } as unknown as TicketActivity], 2, 25)
    test.mount()
    await screen.findByText('<img src=x onerror=alert(1)>')
    const timeline = await screen.findByRole('region', { name: 'Lịch sử hoạt động' })
    expect(await within(timeline).findByText('Mới → Đang xử lý')).toBeInTheDocument()
    expect(within(timeline).getByText('<script>secret</script>')).toBeInTheDocument()
    expect(screen.queryByText('must-not-render')).not.toBeInTheDocument()
    expect(screen.queryByText('private-comment-content')).not.toBeInTheDocument()
    expect(document.querySelector('img[src="x"],script')).not.toBeInTheDocument()
    expect(test.requests.find(item => item.url?.endsWith('/activities'))?.params).toEqual({ page: 2, pageSize: 20, descending: true, isInternal: false })
    expect(test.requests.find(item => item.url === `/api/v1/tickets/${ticketId}`)?.params).toBeUndefined()
    await userEvent.click(within(timeline).getByRole('button', { name: 'Trang trước' }))
    await waitFor(() => expect(test.router.state.location.search).toBe(''))
  })

  it('preserves the list URL in the detail backlink', async () => {
    const test = await openTickets('/tickets?search=VPN&status=2&page=3')
    test.mount()
    await userEvent.click(await screen.findByRole('link', { name: /HD-000042/ }))
    await screen.findByText(ticket.description)
    expect(screen.getByRole('link', { name: '← Danh sách yêu cầu' })).toHaveAttribute('href', '/tickets?search=VPN&status=2&page=3')
  })

  it('does not display cached detail while opening another Ticket', async () => {
    const test = await openTickets(`/tickets/${ticketId}`)
    test.mount()
    await screen.findByText(ticket.description)
    test.backend.accessStatus = 404
    await act(async () => { await test.router.navigate(`/tickets/${otherId}`) })
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
  })

  it('shows a recoverable activity error separately but hides detail on activity ACL rejection', async () => {
    const test = await openTickets(`/tickets/${ticketId}`)
    test.backend.activityStatus = 500
    test.mount()
    await screen.findByRole('alert')
    expect(screen.getByText(ticket.description)).toBeInTheDocument()
    test.backend.activityStatus = 404
    await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }))
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(screen.queryByText(ticket.description)).not.toBeInTheDocument()
  })
})
