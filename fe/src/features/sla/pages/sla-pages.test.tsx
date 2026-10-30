import { QueryClientProvider } from '@tanstack/react-query'
import { expectOnlyCurrentNotificationCount } from '../../../test/notification-cache'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { appRoutes } from '../../../app/router/routes'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { ApiError } from '../../../shared/api/api-error'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import { slaKeys } from '../hooks/use-sla'
import type { SlaPolicy } from '../types/sla'

const id = 'b0000000-0000-0000-0000-000000000001'
const policy: SlaPolicy = { id, name: 'Normal 24/7', priority: 2, firstResponseMinutes: 60, resolutionMinutes: 240, isActive: true,
  createdAt: '2026-10-29T00:00:00Z', updatedAt: null, version: 81 }
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()) })
async function fixture(path = '/admin/sla-policies', manage = true) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: { policy: SlaPolicy; permissions: string[]; readStatus: number; writeStatus: number; writeCode: string; failure?: ApiError
    gate?: Promise<SlaPolicy>; profileGate?: Promise<void>; listGate?: Promise<void> } = {
    policy, permissions: manage ? ['ticket.manage.sla'] : ['ticket.read.own'], readStatus: 200, writeStatus: 200, writeCode: 'VersionConflict',
  }
  const profile = () => ({ ...userA, roles: ['SystemAdmin', 'CustomRole'], permissions: backend.permissions })
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status,
        headers: new AxiosHeaders({ 'Retry-After': '30' }), data: { title: 'Policy bị từ chối.', code: backend.writeCode, traceId: 'sla-trace', errors: { Name: ['Tên policy chưa hợp lệ.'] } } })
    }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('sla', profile()) }
    if (config.url === '/api/v1/users/me') { await backend.profileGate; return { ...response, data: profile() } }
    if (config.url === '/api/v1/notifications/unread-count') return { ...response, data: { count: 0 } }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.startsWith('/api/v1/admin/sla-policies')) {
      if (config.method !== 'get') {
        if (backend.failure) throw backend.failure
        fail(backend.writeStatus)
        if (backend.gate) return { ...response, data: await backend.gate }
        const body = JSON.parse(config.data)
        backend.policy = { ...backend.policy, ...body, version: backend.policy.version + 1 }
        return { ...response, status: config.method === 'post' ? 201 : 200, data: backend.policy }
      }
      fail(backend.readStatus)
      if (config.url === '/api/v1/admin/sla-policies') {
        await backend.listGate
        return { ...response, data: { items: config.params.page === 1 ? [backend.policy] : [], page: config.params.page, pageSize: config.params.pageSize, totalCount: 25 } }
      }
      return { ...response, data: { ...backend.policy } }
    }
    throw new Error('Unexpected SLA fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: userA.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { backend, requests, services, router, mount, writes: () => requests.filter(request => request.url?.includes('/sla-policies') && request.method !== 'get') }
}
async function fillCreate() {
  await userEvent.type(await screen.findByRole('textbox', { name: 'Tên policy' }), 'Cam kết mới')
  await userEvent.type(screen.getByRole('spinbutton', { name: 'Phản hồi đầu tiên (phút)' }), '30')
  await userEvent.type(screen.getByRole('spinbutton', { name: 'Giải quyết (phút)' }), '120')
}
async function save(creating = false) {
  await userEvent.click(screen.getByRole('button', { name: creating ? 'Tạo policy' : 'Lưu policy' }))
  await userEvent.click(within(screen.getByRole('dialog', { name: creating ? 'Xác nhận tạo policy SLA' : 'Xác nhận lưu policy SLA' })).getByRole('button', { name: 'Xác nhận lưu' }))
}

describe('SLA policy permission, pagination and forms', () => {
  it.each(['/admin/sla-policies', '/admin/sla-policies/new', `/admin/sla-policies/${id}`])('guards %s without fetching private data, even for SystemAdmin role text', async path => {
    const test = await fixture(path, false); test.mount()
    await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    expect(test.requests.some(request => request.url?.includes('/sla-policies'))).toBe(false)
    expect(screen.queryByRole('link', { name: 'Chính sách SLA' })).not.toBeInTheDocument()
  })
  it('supports the dedicated grant without Ticket read rights and uses only server paging on the URL', async () => {
    const test = await fixture('/admin/sla-policies?page=2&pageSize=10'); test.mount()
    await screen.findByText('Trang 2 / 3 · 25 kết quả')
    expect(test.requests.find(request => request.url === '/api/v1/admin/sla-policies')?.params).toEqual({ page: 2, pageSize: 10 })
    await userEvent.click(screen.getByRole('button', { name: 'Trang trước' }))
    await screen.findByRole('link', { name: policy.name })
    expect(test.router.state.location.search).toBe('?pageSize=10')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Policy mỗi trang' }), '50')
    await waitFor(() => expect(test.requests.filter(request => request.url === '/api/v1/admin/sla-policies').at(-1)?.params).toEqual({ page: 1, pageSize: 50 }))
    expect(screen.getByRole('link', { name: 'Chính sách SLA' })).toBeInTheDocument()
  })
  it('rejects invalid URL pagination and invalid policy IDs before an API read', async () => {
    const test = await fixture('/admin/sla-policies?page=0'); test.mount()
    await screen.findByText('Phân trang SLA không hợp lệ.')
    expect(test.requests.some(request => request.url?.includes('/sla-policies'))).toBe(false)
    await act(async () => { await test.router.navigate('/admin/sla-policies/invalid') })
    await screen.findByRole('heading', { name: 'Không tìm thấy policy SLA' })
    expect(test.requests.some(request => request.url?.includes('/sla-policies'))).toBe(false)
  })
  it('validates required targets, integer bounds and response <= resolution without sending commands', async () => {
    const test = await fixture('/admin/sla-policies/new'); test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Tạo policy' }))
    expect(screen.getByText('Nhập tên policy, tối đa 200 ký tự.')).toBeInTheDocument()
    await fillCreate()
    const resolution = screen.getByRole('spinbutton', { name: 'Giải quyết (phút)' })
    await userEvent.clear(resolution); await userEvent.type(resolution, '10')
    await userEvent.click(screen.getByRole('button', { name: 'Tạo policy' }))
    expect(screen.getByText(/Mục tiêu giải quyết phải lớn hơn hoặc bằng/)).toBeInTheDocument()
    await userEvent.clear(resolution); await userEvent.type(resolution, '525601')
    await userEvent.click(screen.getByRole('button', { name: 'Tạo policy' }))
    expect(screen.getByText('Nhập số phút nguyên từ 1 đến 525.600.')).toBeInTheDocument()
    expect(test.writes()).toHaveLength(0)
  })
  it('creates an active policy using only the four accepted fields and opens its returned detail', async () => {
    const test = await fixture('/admin/sla-policies/new'); test.mount(); await fillCreate(); await save(true)
    await screen.findByRole('heading', { name: 'Cam kết mới' })
    expect(test.router.state.location.pathname).toBe(`/admin/sla-policies/${id}`)
    expect(JSON.parse(test.writes()[0]!.data)).toEqual({ name: 'Cam kết mới', priority: 2, firstResponseMinutes: 30, resolutionMinutes: 120 })
    expect(test.writes()[0]!.headers.get('Authorization')).toBe('Bearer access-sla')
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
  })
  it('keeps create draft on duplicate active priority and lets the user choose another priority', async () => {
    const test = await fixture('/admin/sla-policies/new'); test.backend.writeStatus = 409; test.backend.writeCode = 'ActivePolicyExists'; test.mount(); await fillCreate(); await save(true)
    await screen.findByText(/Mức ưu tiên này đã có policy đang hoạt động/)
    expect(screen.getByRole('textbox', { name: 'Tên policy' })).toHaveValue('Cam kết mới')
    expect(screen.getByText('Mã tra cứu: sla-trace')).toBeInTheDocument()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Mức ưu tiên' }), '3'); test.backend.writeStatus = 200
    await save(true); await screen.findByRole('heading', { name: 'Cam kết mới' })
    expect(JSON.parse(test.writes()[1]!.data).priority).toBe(3)
  })
  it('updates using the current server version and toggles status without persisting an unsaved metadata draft', async () => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.mount()
    const name = await screen.findByRole('textbox', { name: 'Tên policy' }); await userEvent.clear(name); await userEvent.type(name, 'Bản nhập chưa lưu')
    await userEvent.click(screen.getByRole('button', { name: 'Ngừng policy' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận trạng thái policy SLA' })).getByRole('button', { name: 'Xác nhận trạng thái' }))
    await screen.findByText('Đã ngừng hoạt động')
    expect(JSON.parse(test.writes()[0]!.data)).toEqual({ isActive: false, version: 81 })
    expect(screen.getByRole('textbox', { name: 'Tên policy' })).toHaveValue('Bản nhập chưa lưu')
    await save(); await screen.findByRole('heading', { name: 'Bản nhập chưa lưu' })
    expect(JSON.parse(test.writes()[1]!.data)).toEqual({ name: 'Bản nhập chưa lưu', priority: 2, firstResponseMinutes: 60, resolutionMinutes: 240, version: 82 })
  })
  it('preserves a conflict draft, reads a fresh version and requires explicit review before a new command', async () => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.backend.writeStatus = 409; test.mount()
    const name = await screen.findByRole('textbox', { name: 'Tên policy' }); await userEvent.type(name, ' sửa'); await save()
    await screen.findByText(/Policy đã thay đổi. Bản nhập được giữ/)
    expect(screen.getByRole('button', { name: 'Lưu policy' })).toBeDisabled()
    test.backend.policy = { ...policy, version: 99, resolutionMinutes: 300 }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại để xem xét' }))
    await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' })
    expect(screen.getByText('300 phút')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Tên policy' })).toHaveValue('Normal 24/7 sửa')
    expect(test.writes()).toHaveLength(1)
    await userEvent.click(screen.getByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' })); test.backend.writeStatus = 200
    await save(); await screen.findByRole('heading', { name: 'Normal 24/7 sửa' })
    expect(JSON.parse(test.writes()[1]!.data).version).toBe(99)
  })
  it('does not overwrite the draft or silently adopt background versions', async () => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.mount()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Tên policy' }), ' nháp')
    test.backend.policy = { ...policy, version: 99, name: 'Khác' }
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: slaKeys.detail(test.services.session.store.getState().scope, id) }) })
    expect(screen.getByRole('textbox', { name: 'Tên policy' })).toHaveValue('Normal 24/7 nháp')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Lưu policy' })).toBeDisabled())
    expect(screen.getByText(/Có phiên bản policy mới/)).toBeInTheDocument()
    expect(test.writes()).toHaveLength(0)
  })
  it('captures the baseline before opening a confirmation even when no field was edited', async () => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Lưu policy' }))
    test.backend.policy = { ...policy, version: 99, name: 'Thay đổi đồng thời' }
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: slaKeys.detail(test.services.session.store.getState().scope, id) }) })
    expect(screen.getByRole('textbox', { name: 'Tên policy' })).toHaveValue(policy.name)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Lưu policy' })).toBeDisabled())
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(test.writes()).toHaveLength(0)
  })
  it.each([400, 429, 503])('handles HTTP %i and retains a draft without automatic POST retries', async status => {
    const test = await fixture('/admin/sla-policies/new'); test.backend.writeStatus = status; test.mount(); await fillCreate(); await save(true)
    await screen.findByText('Mã tra cứu: sla-trace')
    expect(screen.getByRole('textbox', { name: 'Tên policy' })).toHaveValue('Cam kết mới')
    expect(test.writes()).toHaveLength(1)
    if (status !== 400) expect(screen.getByRole('button', { name: 'Tạo policy' })).toBeDisabled()
    if (status === 400) expect(screen.getByText('Tên policy chưa hợp lệ.')).toBeInTheDocument()
  })
  it('blocks ambiguous timeouts even after explicit read/review', async () => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.backend.failure = new ApiError('Timeout', 'timeout'); test.mount()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Tên policy' }), ' nháp'); await save()
    await screen.findByText(/Chưa xác định được kết quả lưu/)
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại để xem xét' }))
    expect(await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' })).toBeDisabled()
    expect(test.writes()).toHaveLength(1)
  })
  it('removes the form and cache after a live grant is removed', async () => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.mount(); await screen.findByRole('textbox', { name: 'Tên policy' })
    test.backend.permissions = ['ticket.read.own']
    await act(async () => { await test.services.session.refreshProfile() })
    await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    expectOnlyCurrentNotificationCount(test.services.queryClient, test.services.session.store.getState().scope)
  })
  it.each([403, 404])('purges a previously read detail after server denial %i without an automatic fetch loop', async status => {
    const test = await fixture(`/admin/sla-policies/${id}`); test.mount(); await screen.findByRole('textbox', { name: 'Tên policy' })
    test.backend.readStatus = status
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: slaKeys.detail(test.services.session.store.getState().scope, id) }) })
    await screen.findByRole('heading', { name: status === 403 ? 'Không có quyền truy cập' : 'Không tìm thấy policy SLA' })
    await waitFor(() => expect(test.services.queryClient.getQueryData(slaKeys.detail(test.services.session.store.getState().scope, id))).toBeUndefined())
    expect(screen.queryByRole('textbox', { name: 'Tên policy' })).not.toBeInTheDocument()
    expect(test.requests.filter(r => r.url === `/api/v1/admin/sla-policies/${id}`)).toHaveLength(2)
  })
  it('discards a late create result after logout without navigation or private cache writes', async () => {
    const test = await fixture('/admin/sla-policies/new'); const gate = deferred<SlaPolicy>(); test.backend.gate = gate.promise; test.mount(); await fillCreate(); await save(true)
    await waitFor(() => expect(test.writes()).toHaveLength(1))
    await act(async () => { await test.services.session.logout() })
    await act(async () => { gate.resolve(policy); await gate.promise })
    expect(test.router.state.location.pathname).toBe('/login')
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
  })
})
