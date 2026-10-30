import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { appRoutes } from '../../../app/router/routes'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { Permission } from '../../auth/lib/permissions'
import type { UserProfile } from '../../auth/types/auth'
import { deferred, tokenPair, userA, userB } from '../../../test/auth-fixtures'
import type { PagedResult } from '../../../shared/api/pagination'
import type { ProblemDetails } from '../../../shared/api/api-error'
import { ticketKeys } from '../hooks/use-tickets'
import { defaultTicketSearch } from '../lib/ticket-search'
import type { TicketCreated } from '../types/ticket'
import type { TicketCategoryLookup } from '../types/ticket-category'
import { noAttachmentUpload } from '../../../test/attachment-fixtures'
import { noSlaForPath } from '../../../test/sla-fixtures'

const rootId = '20000000-0000-0000-0000-000000000001'
const leafId = '20000000-0000-0000-0000-000000000002'
const childId = '20000000-0000-0000-0000-000000000003'
const nextPageId = '20000000-0000-0000-0000-000000000004'
const ticketId = '30000000-0000-0000-0000-000000000001'
const root: TicketCategoryLookup = { id: rootId, parentId: null, name: 'Công nghệ', hasChildren: true }
const leaf: TicketCategoryLookup = { id: leafId, parentId: null, name: 'Hành chính', hasChildren: false }
const child: TicketCategoryLookup = { id: childId, parentId: rootId, name: 'Mạng nội bộ', hasChildren: false }
const nextPage: TicketCategoryLookup = { id: nextPageId, parentId: null, name: 'Thiết bị', hasChildren: false }
const categoryPage = (items: TicketCategoryLookup[], page = 1, totalCount = items.length): PagedResult<TicketCategoryLookup> =>
  ({ items, page, pageSize: 20, totalCount })
const receipt: TicketCreated = { id: ticketId, ticketNumber: 123, requesterId: userA.id, createdByUserId: userA.id,
  categoryId: leafId, status: 1, priority: 2, createdAt: '2026-10-29T10:00:00Z', version: 91 }
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); for (const dispose of disposals.splice(0)) dispose(); vi.useRealTimers() })

async function createFixture(profile: UserProfile = userA, login = true, path = '/tickets/new') {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: {
    profile: UserProfile
    roots: PagedResult<TicketCategoryLookup>
    lookupStatus: number
    lookupGate?: Promise<PagedResult<TicketCategoryLookup>>
    childGate?: Promise<PagedResult<TicketCategoryLookup>>
    createStatus: number
    createFailure?: unknown
    createGate?: Promise<TicketCreated>
    receipt: TicketCreated
    problem: ProblemDetails
    detailStatus: number
  } = { profile, roots: categoryPage([root, leaf], 1, 21), lookupStatus: 200, createStatus: 201,
    receipt, problem: { title: 'Dữ liệu không hợp lệ.', traceId: 'create-trace' }, detailStatus: 200 }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const rejectStatus = (status: number) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined,
        { ...response, status, headers: new AxiosHeaders({ 'Retry-After': '2' }), data: backend.problem })
    }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('create', backend.profile) }
    if (config.url === '/api/v1/users/me') return { ...response, data: backend.profile }
    if (config.url === '/api/v1/auth/refresh') return { ...response, data: tokenPair('rotated', backend.profile) }
    if (config.url === '/api/v1/notifications/unread-count') return { ...response, data: { count: 0 } }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.endsWith('/sla')) return { ...response, data: noSlaForPath(config.url) }
    if (config.url?.endsWith('/attachments/options')) return { ...response, data: noAttachmentUpload }
    if (config.url?.endsWith('/attachments')) return { ...response, data: { items: [], page: 1, pageSize: 10, totalCount: 0 } }
    if (config.url?.endsWith('/workflow')) return { ...response, data: { version: receipt.version, status: 1, canAssign: false, assignmentIsValid: false, transitions: [], assignedTeamName: null, assignedAgentName: null } }
    if (config.url?.endsWith('/discussion-access')) return { ...response, data: { canReadInternal: false, canCommentPublic: false, canCommentInternal: false } }
    if (config.url?.endsWith('/comments')) return { ...response, data: { items: [], page: 1, pageSize: 20, totalCount: 0 } }
    if (config.url === '/api/v1/ticket-categories/lookup') {
      rejectStatus(backend.lookupStatus)
      const data = backend.lookupGate ? await backend.lookupGate : config.params.parentId
        ? backend.childGate ? await backend.childGate : categoryPage([child])
        : config.params.page === 2 ? categoryPage([nextPage], 2, 21)
          : config.params.search ? categoryPage(backend.roots.items.filter(item => item.name.includes(String(config.params.search)))) : backend.roots
      return { ...response, data }
    }
    if (config.url === '/api/v1/tickets' && config.method === 'post') {
      if (backend.createFailure) throw backend.createFailure
      rejectStatus(backend.createStatus)
      return { ...response, status: backend.createStatus, data: backend.createGate ? await backend.createGate : backend.receipt }
    }
    if (config.url === `/api/v1/tickets/${ticketId}/access`) { rejectStatus(backend.detailStatus); return { ...response, data: { canRead: true, canAssign: false, canUpdateStatus: false } } }
    if (config.url === `/api/v1/tickets/${ticketId}`) return { ...response, data: { ...receipt, title: 'Ticket đã tạo', description: 'Nội dung từ server', assignedTeamId: null,
      assignedAgentId: null, updatedAt: null, resolutionSummary: null, resolvedAt: null, closedAt: null } }
    if (config.url?.endsWith('/activities')) return { ...response, data: { items: [], page: 1, pageSize: 20, totalCount: 0 } }
    throw new Error('Unexpected fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  if (login) await services.session.login({ email: profile.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}>
    <AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider>
  </QueryClientProvider>)
  const posts = () => requests.filter(config => config.url === '/api/v1/tickets' && config.method === 'post')
  return { services, router, backend, requests, posts, mount }
}
async function fillTicket(categoryName = 'Hành chính') {
  await userEvent.type(screen.getByRole('textbox', { name: 'Tiêu đề' }), '  Không kết nối VPN  ')
  await userEvent.type(screen.getByRole('textbox', { name: 'Nội dung yêu cầu' }), '  Mô tả lỗi chi tiết.  ')
  await userEvent.click(await screen.findByRole('radio', { name: categoryName }))
}

describe('create Ticket routing and paged category picker', () => {
  it('requires a session before fetching lookup and preserves the direct link', async () => {
    const test = await createFixture(userA, false)
    test.mount()
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    expect(test.router.state.location.search).toContain('returnTo=%2Ftickets%2Fnew')
    expect(test.requests).toHaveLength(0)
  })
  it('does not grant creation from a read permission or SystemAdmin role', async () => {
    const test = await createFixture({ ...userA, roles: ['SystemAdmin'], permissions: [Permission.TicketReadAll] })
    test.mount()
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(test.requests.some(config => config.url?.includes('ticket-categories'))).toBe(false)
  })
  it('loads roots lazily, pages the current branch and opens children only for hasChildren nodes', async () => {
    const test = await createFixture()
    test.mount()
    await screen.findByRole('radio', { name: 'Hành chính' })
    const lookup = () => test.requests.filter(config => config.url?.endsWith('/lookup')).at(-1)
    expect(lookup()?.params).toEqual({ page: 1, pageSize: 20, search: undefined, rootsOnly: true, parentId: undefined })
    expect(screen.queryByRole('button', { name: 'Mở danh mục Hành chính' })).not.toBeInTheDocument()
    expect(test.requests.some(config => config.params?.parentId)).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Trang sau' }))
    await screen.findByRole('radio', { name: 'Thiết bị' })
    expect(lookup()?.params.page).toBe(2)
    await userEvent.click(screen.getByRole('radio', { name: 'Thiết bị' }))
    await userEvent.click(screen.getByRole('button', { name: 'Danh mục gốc' }))
    await screen.findByRole('radio', { name: 'Công nghệ' })
    expect(screen.getByText('Đã chọn: Thiết bị')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Mở danh mục Công nghệ' }))
    await screen.findByRole('radio', { name: 'Mạng nội bộ' })
    expect(lookup()?.params).toMatchObject({ parentId: rootId, rootsOnly: false, page: 1 })
    await userEvent.click(screen.getByRole('radio', { name: 'Mạng nội bộ' }))
    expect(screen.getByText('Đã chọn: Công nghệ / Mạng nội bộ')).toBeInTheDocument()
    expect(test.requests.filter(config => config.url?.includes('ticket-categories')).every(config => config.url?.endsWith('/lookup'))).toBe(true)
  })
  it('searches within the current level without submitting the Ticket when pressing Enter', async () => {
    const test = await createFixture()
    test.mount()
    await screen.findByRole('radio', { name: 'Hành chính' })
    await userEvent.click(screen.getByRole('button', { name: 'Trang sau' }))
    await screen.findByRole('radio', { name: 'Thiết bị' })
    await userEvent.type(screen.getByRole('searchbox', { name: 'Tìm danh mục trong cấp đang mở' }), 'Hành{Enter}')
    await screen.findByRole('radio', { name: 'Hành chính' })
    expect(test.requests.filter(config => config.url?.endsWith('/lookup')).at(-1)?.params).toMatchObject({ page: 1, search: 'Hành', rootsOnly: true })
    expect(test.posts()).toHaveLength(0)
  })
  it('hides a stale branch response when a user returns to roots before it arrives', async () => {
    const test = await createFixture()
    const gate = deferred<PagedResult<TicketCategoryLookup>>()
    test.backend.childGate = gate.promise
    test.mount()
    await screen.findByRole('radio', { name: 'Hành chính' })
    await userEvent.click(screen.getByRole('button', { name: 'Mở danh mục Công nghệ' }))
    await screen.findByText('Đang tải danh mục…')
    const childRequest = test.requests.find(config => config.params?.parentId === rootId)
    await userEvent.click(screen.getByRole('button', { name: 'Danh mục gốc' }))
    await screen.findByRole('radio', { name: 'Hành chính' })
    await act(async () => { gate.resolve(categoryPage([child])); await gate.promise })
    expect(childRequest?.signal?.aborted).toBe(true)
    expect(screen.queryByRole('radio', { name: 'Mạng nội bộ' })).not.toBeInTheDocument()
  })
  it('handles lookup loading, empty and errors without fabricating a category or requiring management grants', async () => {
    const test = await createFixture({ ...userA, permissions: [Permission.TicketCreate] })
    const gate = deferred<PagedResult<TicketCategoryLookup>>()
    test.backend.lookupGate = gate.promise
    test.mount()
    expect(screen.getByText('Đang tải danh mục…')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Tạo yêu cầu' })).toBeInTheDocument()
    await act(async () => { gate.resolve(categoryPage([])); await gate.promise })
    await screen.findByRole('heading', { name: 'Không có danh mục phù hợp' })
    test.backend.lookupGate = undefined
    test.backend.lookupStatus = 503
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: ['ticket-category-lookup'] }) })
    expect(await screen.findByRole('alert')).toHaveTextContent('create-trace')
    test.backend.lookupStatus = 200
    await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }))
    await screen.findByRole('radio', { name: 'Hành chính' })
  })
})

describe('create Ticket command, identity and result handling', () => {
  it('validates whitespace/required fields before posting', async () => {
    const test = await createFixture()
    test.mount()
    await userEvent.type(screen.getByRole('textbox', { name: 'Tiêu đề' }), '   ')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('textbox', { name: 'Nội dung yêu cầu' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByText('Vui lòng chọn danh mục khả dụng.')).toBeInTheDocument()
    expect(test.posts()).toHaveLength(0)
    await userEvent.type(screen.getByRole('textbox', { name: 'Tiêu đề' }), 'VPN')
    expect(screen.queryByText('Vui lòng nhập tiêu đề.')).not.toBeInTheDocument()
  })
  it('posts only the DTO, invalidates lists of this session and opens server detail after 201', async () => {
    const test = await createFixture()
    const scope = test.services.session.store.getState().scope
    const key = ticketKeys.list(scope, defaultTicketSearch)
    const otherKey = ticketKeys.list('another-scope', defaultTicketSearch)
    test.services.queryClient.setQueryData(key, { items: [], totalCount: 0, page: 1, pageSize: 20 })
    test.services.queryClient.setQueryData(otherKey, { items: [], totalCount: 0, page: 1, pageSize: 20 })
    test.mount()
    const navigation = screen.getByRole('navigation', { name: 'Điều hướng chính' })
    expect(within(navigation).getByRole('link', { name: 'Tạo yêu cầu' })).toHaveAttribute('aria-current', 'page')
    expect(within(navigation).getByRole('link', { name: 'Danh sách yêu cầu' })).not.toHaveAttribute('aria-current')
    await fillTicket()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Độ ưu tiên' }), '4')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByRole('heading', { name: 'Ticket đã tạo' })
    expect(test.router.state.location.pathname).toBe(`/tickets/${ticketId}`)
    expect(test.posts()).toHaveLength(1)
    expect(JSON.parse(test.posts()[0]?.data)).toEqual({ title: 'Không kết nối VPN', description: 'Mô tả lỗi chi tiết.', categoryId: leafId, priority: 4 })
    expect(test.posts()[0]?.headers.get('Authorization')).toBe('Bearer access-create')
    expect(test.services.queryClient.getQueryState(key)?.isInvalidated).toBe(true)
    expect(test.services.queryClient.getQueryState(otherKey)?.isInvalidated).toBe(false)
    expect(screen.getByText('HD-000123')).toBeInTheDocument()
  })
  it('allows choosing an available parent and keeps the 201 receipt when create does not include read permission', async () => {
    const test = await createFixture({ ...userA, roles: ['Custom'], permissions: [Permission.TicketCreate] })
    test.mount()
    await fillTicket('Công nghệ')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByRole('heading', { name: 'Tạo yêu cầu thành công' })
    expect(JSON.parse(test.posts()[0]?.data).categoryId).toBe(rootId)
    expect(screen.getByText(/Yêu cầu HD-000123 đã được ghi nhận/)).toBeInTheDocument()
    expect(test.requests.some(config => config.url?.startsWith('/api/v1/tickets/'))).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Tạo yêu cầu khác' }))
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('')
    expect(test.posts()).toHaveLength(1)
  })
  it('blocks double submission and disables controls while waiting for a receipt', async () => {
    const test = await createFixture()
    const gate = deferred<TicketCreated>()
    test.backend.createGate = gate.promise
    test.mount()
    await fillTicket()
    const form = screen.getByRole('form', { name: 'Tạo yêu cầu hỗ trợ' })
    act(() => { fireEvent.submit(form); fireEvent.submit(form) })
    await waitFor(() => expect(test.posts()).toHaveLength(1))
    expect(screen.getByRole('button', { name: 'Gửi yêu cầu' })).toBeDisabled()
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toBeDisabled()
    expect(screen.getByRole('radio', { name: 'Hành chính' })).toBeDisabled()
    await act(async () => { gate.resolve(receipt); await gate.promise })
    await screen.findByRole('heading', { name: 'Ticket đã tạo' })
  })
  it('keeps draft values and maps server ValidationProblemDetails to fields', async () => {
    const test = await createFixture()
    test.backend.createStatus = 400
    test.backend.problem.errors = { Title: ['Tiêu đề không hợp lệ.'], '$.description': ['Nội dung không hợp lệ.'] }
    test.mount()
    await fillTicket()
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByText('Tiêu đề không hợp lệ.')
    expect(screen.getByText('Nội dung không hợp lệ.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('  Không kết nối VPN  ')
    expect(screen.getByRole('button', { name: 'Gửi yêu cầu' })).toBeEnabled()
    expect(screen.getByText(/create-trace/)).toBeInTheDocument()
  })
  it('clears an unavailable category, refreshes lookup and retains the text draft', async () => {
    const test = await createFixture()
    test.backend.createStatus = 400
    test.backend.problem.code = 'InvalidCategory'
    test.mount()
    await fillTicket()
    test.backend.roots = categoryPage([root])
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByText('Danh mục không còn khả dụng. Vui lòng chọn lại.')
    await waitFor(() => expect(screen.queryByRole('radio', { name: 'Hành chính' })).not.toBeInTheDocument())
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('  Không kết nối VPN  ')
    expect(screen.getByText('Chưa chọn danh mục.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    expect(test.posts()).toHaveLength(1)
  })
  it.each(['timeout', 'network', 'server'])('does not retry an uncertain %s result and requires an explicit decision before resubmit', async kind => {
    const test = await createFixture()
    if (kind === 'server') test.backend.createStatus = 503
    else test.backend.createFailure = new AxiosError('Uncertain response', kind === 'timeout' ? 'ECONNABORTED' : 'ERR_NETWORK')
    test.mount()
    await fillTicket()
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByRole('heading', { name: 'Chưa xác định được kết quả tạo' })
    const form = screen.getByRole('form', { name: 'Tạo yêu cầu hỗ trợ' })
    fireEvent.submit(form)
    expect(test.posts()).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Gửi yêu cầu' })).toBeDisabled()
    expect(test.requests.some(config => config.url === '/api/v1/auth/refresh')).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Tôi đã kiểm tra, cho phép gửi lại' }))
    expect(test.posts()).toHaveLength(1)
    test.backend.createFailure = undefined
    test.backend.createStatus = 201
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByRole('heading', { name: 'Ticket đã tạo' })
    expect(test.posts()).toHaveLength(2)
  })
  it('honors Retry-After on 429 and enables only a deliberate retry after the cooldown', async () => {
    const test = await createFixture()
    test.backend.createStatus = 429
    test.mount()
    await fillTicket()
    vi.useFakeTimers()
    await act(async () => { fireEvent.submit(screen.getByRole('form', { name: 'Tạo yêu cầu hỗ trợ' })); await vi.advanceTimersByTimeAsync(1) })
    expect(screen.getByText('Vui lòng chờ 2 giây trước khi thử lại.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Gửi yêu cầu' })).toBeDisabled()
    await act(async () => { await vi.advanceTimersByTimeAsync(2_000) })
    expect(screen.getByRole('button', { name: 'Gửi yêu cầu' })).toBeEnabled()
    expect(test.posts()).toHaveLength(1)
  })
  it('drops a late receipt after logout and a different user logs in', async () => {
    const test = await createFixture()
    const gate = deferred<TicketCreated>()
    test.backend.createGate = gate.promise
    test.mount()
    await fillTicket()
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await waitFor(() => expect(test.posts()).toHaveLength(1))
    await act(async () => { await test.services.session.logout() })
    test.backend.profile = userB
    await act(async () => {
      await test.services.session.login({ email: userB.email, password: 'test-only' })
      await test.router.navigate('/account')
    })
    await act(async () => { gate.resolve(receipt); await gate.promise })
    expect(test.posts()[0]?.signal?.aborted).toBe(true)
    expect(test.router.state.location.pathname).toBe('/account')
    expect(screen.queryByRole('heading', { name: 'Ticket đã tạo' })).not.toBeInTheDocument()
    expect(test.services.queryClient.getQueryCache().getAll().some(query => query.queryKey.includes(ticketId))).toBe(false)
  })
  it('does not turn a confirmed creation into another POST if the detail subsequently returns 404', async () => {
    const test = await createFixture()
    test.backend.detailStatus = 404
    test.mount()
    await fillTicket()
    await userEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }))
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(test.posts()).toHaveLength(1)
    expect(test.router.state.location.pathname).toBe(`/tickets/${ticketId}`)
  })
  it('does not remount the draft when current profile is rechecked in the background', async () => {
    const test = await createFixture()
    test.mount()
    await fillTicket()
    await act(async () => { await test.services.session.refreshProfile({ background: true }) })
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('  Không kết nối VPN  ')
    expect(within(screen.getByRole('group', { name: 'Danh mục yêu cầu' })).getByRole('radio', { name: 'Hành chính' })).toBeChecked()
  })
})

