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
import { ApiError } from '../../../shared/api/api-error'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import type { UserProfile } from '../../auth/types/auth'
import type { NotificationDetails } from '../types/notification'
import { notificationKeys } from '../hooks/use-notifications'

const id = 'c0000000-0000-0000-0000-000000000001'
const other = 'c0000000-0000-0000-0000-000000000002'
const ticketId = 'd0000000-0000-0000-0000-000000000001'
const item: NotificationDetails = { id, ticketId, activityId: other, kind: 'PublicCommentAdded', isInternal: false,
  title: 'Ticket có bình luận mới', createdAt: '2026-10-30T01:00:00Z', readAt: null }
const note: NotificationDetails = { ...item, id: other, isInternal: true, kind: 'InternalNoteAdded', title: 'Ticket có ghi chú nội bộ mới' }
const root = '/api/v1/notifications'
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()) })
async function fixture(path = '/notifications', anonymous = false) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: { rows: NotificationDetails[]; profile: UserProfile; readStatus: number; writeStatus: number; countStatus: number; ticketStatus: number;
    failure?: ApiError; gate?: Promise<NotificationDetails>; listGate?: Promise<void>; countGate?: Promise<void>; profileGate?: Promise<void>; receipt?: NotificationDetails; expireOnce?: boolean } = {
    rows: structuredClone([item, note]), profile: { ...userA, roles: [], permissions: ['ticket.read.own'] }, readStatus: 200, writeStatus: 200, countStatus: 200, ticketStatus: 404,
  }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status,
        headers: new AxiosHeaders({ 'Retry-After': '30' }), data: { title: 'Thông báo bị từ chối.', traceId: 'notification-trace' } })
    }
    if (config.url === '/api/v1/auth/login' || config.url === '/api/v1/auth/refresh') return { ...response, data: tokenPair(config.url.endsWith('/refresh') ? 'rotated' : 'notifications', backend.profile) }
    if (config.url === '/api/v1/users/me') { await backend.profileGate; return { ...response, data: backend.profile } }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.startsWith(root)) {
      if (backend.expireOnce) { backend.expireOnce = false; fail(401) }
      if (config.url === `${root}/unread-count`) {
        const count = backend.rows.filter(row => row.readAt === null).length
        await backend.countGate; fail(backend.countStatus); return { ...response, data: { count } }
      }
      if (config.method === 'put') {
        if (backend.failure) throw backend.failure
        fail(backend.writeStatus)
        const selected = backend.rows.find(row => `${root}/${row.id}/read` === config.url)
        if (!selected) { fail(404); throw new Error('Unreachable') }
        selected.readAt ??= '2026-10-30T02:00:00Z'
        return { ...response, data: backend.gate ? await backend.gate : backend.receipt ?? { ...selected } }
      }
      fail(backend.readStatus)
      if (config.url === root) {
        const params = config.params
        const rows = structuredClone(backend.rows.filter(row => params.unread === undefined || (row.readAt === null) === params.unread))
        await backend.listGate
        return { ...response, data: { items: rows.slice((params.page - 1) * params.pageSize, params.page * params.pageSize), page: params.page, pageSize: params.pageSize, totalCount: rows.length } }
      }
      const selected = backend.rows.find(row => `${root}/${row.id}` === config.url)
      if (!selected) fail(404)
      return { ...response, data: selected ? { ...selected } : null }
    }
    if (config.url?.startsWith('/api/v1/tickets/')) { fail(backend.ticketStatus); throw new Error('Unreachable ticket fixture') }
    throw new Error('Unexpected notification route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  if (!anonymous) await services.session.login({ email: userA.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { backend, services, router, requests, mount, writes: () => requests.filter(r => r.method === 'put') }
}
describe('persisted notification inbox, count and detail', () => {
  it('redirects anonymous deep links before any notification request', async () => {
    const test = await fixture(`/notifications/${id}`, true); test.mount()
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    expect(test.requests).toHaveLength(0)
  })
  it('does not fetch notifications while current profile is unresolved', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.profileGate = gate.promise
    let refresh!: Promise<unknown>
    act(() => { refresh = test.services.session.refreshProfile() }); test.mount()
    expect(test.requests.some(r => r.url?.startsWith(root))).toBe(false)
    await act(async () => { gate.resolve(); await refresh })
    await screen.findByRole('link', { name: item.title })
  })
  it('shows persisted rows, visibility and the server count without requiring an admin role or a hub', async () => {
    const test = await fixture(); test.mount()
    await screen.findByRole('link', { name: item.title })
    await screen.findByRole('link', { name: 'Thông báo: 2 chưa đọc' })
    expect(screen.getByText('Nội bộ')).toBeInTheDocument()
    expect(test.requests.find(r => r.url === root)?.headers.get('Authorization')).toBe('Bearer access-notifications')
    expect(test.requests.some(r => r.url?.includes('/hubs/'))).toBe(false)
    expect(test.requests.some(r => r.url?.startsWith('/api/v1/tickets'))).toBe(false)
  })
  it('uses server filter/page/pageSize on the URL and supports back navigation', async () => {
    const test = await fixture('/notifications?page=2&pageSize=1&unread=true'); test.mount()
    await screen.findByRole('link', { name: note.title })
    expect(test.requests.find(r => r.url === root)?.params).toEqual({ page: 2, pageSize: 1, unread: true })
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Trạng thái đọc' }), 'false')
    await screen.findByRole('heading', { name: 'Không có thông báo trong trang này' })
    expect(test.router.state.location.search).toBe('?unread=false&pageSize=1')
    await act(async () => { await test.router.navigate(-1) })
    await screen.findByRole('link', { name: note.title })
    expect(test.router.state.location.search).toContain('page=2')
  })
  it.each(['page=0', 'pageSize=101', 'unread=invalid', 'unread=true&unread=false'])('rejects invalid search %s before list fetch', async search => {
    const test = await fixture(`/notifications?${search}`); test.mount()
    await screen.findByText('Bộ lọc hoặc phân trang thông báo không hợp lệ.')
    expect(test.requests.some(r => r.url === root)).toBe(false)
  })
  it('distinguishes no notifications from an unavailable count instead of displaying a false zero', async () => {
    const test = await fixture(); test.backend.rows = []; test.backend.countStatus = 503; test.mount()
    await screen.findByRole('heading', { name: 'Không có thông báo trong trang này' })
    await screen.findByRole('link', { name: 'Thông báo: chưa xác nhận số chưa đọc' })
    expect(screen.queryByRole('link', { name: 'Thông báo: 0 chưa đọc' })).not.toBeInTheDocument()
  })
  it('marks read with a bodyless PUT then refetches unread list and count instead of decrementing locally', async () => {
    const test = await fixture('/notifications?unread=true'); test.mount(); await screen.findByRole('link', { name: item.title })
    await userEvent.click(screen.getByRole('button', { name: `Đánh dấu đã đọc: ${item.title}` }))
    await screen.findByText('Đã đánh dấu thông báo là đã đọc.')
    await screen.findByRole('link', { name: 'Thông báo: 1 chưa đọc' })
    expect(screen.queryByRole('link', { name: item.title })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: note.title })).toBeInTheDocument()
    expect(test.writes()).toHaveLength(1)
    expect(test.writes()[0]?.data).toBeUndefined()
    expect(test.writes()[0]?.url).toBe(`${root}/${id}/read`)
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
  })
  it('opens detail with its filtered return link and does not mark read just by viewing', async () => {
    const test = await fixture('/notifications?unread=true'); test.mount()
    await userEvent.click(await screen.findByRole('link', { name: item.title }))
    await screen.findByRole('heading', { name: item.title })
    expect(await screen.findByRole('link', { name: 'Về danh sách thông báo' })).toHaveAttribute('href', '/notifications?unread=true')
    expect(await screen.findByRole('link', { name: 'Mở yêu cầu liên quan' })).toHaveAttribute('href', `/tickets/${ticketId}`)
    expect(test.writes()).toHaveLength(0)
    await userEvent.click(screen.getByRole('button', { name: 'Đánh dấu đã đọc' }))
    await screen.findByText('Đã đánh dấu thông báo là đã đọc.')
    expect(screen.queryByRole('button', { name: 'Đánh dấu đã đọc' })).not.toBeInTheDocument()
  })
  it('checks current Ticket access on navigation and handles access loss without showing Ticket data', async () => {
    const test = await fixture(`/notifications/${id}`); test.mount()
    await userEvent.click(await screen.findByRole('link', { name: 'Mở yêu cầu liên quan' }))
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(test.requests.some(r => r.url === `/api/v1/tickets/${ticketId}/access`)).toBe(true)
  })
  it.each([403, 404])('removes a previously visible detail and cache on HTTP %i', async status => {
    const test = await fixture(`/notifications/${id}`); test.mount(); await screen.findByRole('heading', { name: item.title })
    test.backend.readStatus = status
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: notificationKeys.detail(test.services.session.store.getState().scope, id) }) })
    await screen.findByRole('heading', { name: 'Thông báo không còn khả dụng' })
    await waitFor(() => expect(test.services.queryClient.getQueryData(notificationKeys.detail(test.services.session.store.getState().scope, id))).toBeUndefined())
    expect(screen.queryByRole('heading', { name: item.title })).not.toBeInTheDocument()
  })
  it('rejects invalid IDs without loading a notification', async () => {
    const test = await fixture('/notifications/not-a-guid'); test.mount()
    await screen.findByRole('heading', { name: 'Thông báo không còn khả dụng' })
    expect(test.requests.some(r => r.url === `${root}/not-a-guid`)).toBe(false)
  })
  it('ignores list data arriving after a filter change even if the adapter ignores cancellation', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.listGate = gate.promise; test.mount()
    await waitFor(() => expect(test.requests.some(r => r.url === root)).toBe(true))
    test.backend.listGate = undefined; test.backend.rows = []
    await act(async () => { await test.router.navigate('/notifications?unread=false') })
    await screen.findByRole('heading', { name: 'Không có thông báo trong trang này' })
    await act(async () => { gate.resolve(); await gate.promise })
    expect(screen.queryByRole('link', { name: item.title })).not.toBeInTheDocument()
  })
  it('drops internal metadata and delayed count on a new permission scope', async () => {
    const test = await fixture(); test.mount(); await screen.findByRole('link', { name: note.title })
    const oldScope = test.services.session.store.getState().scope
    const gate = deferred<void>(); test.backend.countGate = gate.promise
    act(() => { void test.services.queryClient.invalidateQueries({ queryKey: notificationKeys.count(oldScope) }) })
    test.backend.profile = { ...test.backend.profile, permissions: [] }; test.backend.rows = []; test.backend.countGate = undefined
    await act(async () => { await test.services.session.refreshProfile(); gate.resolve(); await gate.promise })
    await screen.findByRole('link', { name: 'Thông báo: 0 chưa đọc' })
    expect(screen.queryByRole('link', { name: note.title })).not.toBeInTheDocument()
    expect(test.services.queryClient.getQueryCache().getAll().some(q => q.queryKey.includes(oldScope))).toBe(false)
  })
  it('discards a late mark-read receipt after logout and never repopulates private cache', async () => {
    const test = await fixture(`/notifications/${id}`); const gate = deferred<NotificationDetails>(); test.backend.gate = gate.promise; test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Đánh dấu đã đọc' }))
    await waitFor(() => expect(test.writes()).toHaveLength(1))
    await act(async () => { await test.services.session.logout(); gate.resolve({ ...item, readAt: '2026-10-30T02:00:00Z' }); await gate.promise })
    expect(test.router.state.location.pathname).toBe('/login')
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
  })
  it.each([400, 429, 503])('handles mark-read HTTP %i without auto replay', async status => {
    const test = await fixture(`/notifications/${id}`); test.backend.writeStatus = status; test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Đánh dấu đã đọc' }))
    await screen.findByText('Mã tra cứu: notification-trace')
    expect(test.writes()).toHaveLength(1)
    if (status !== 400) await waitFor(() => expect(screen.getByRole('button', { name: 'Đánh dấu đã đọc' })).toBeDisabled())
  })
  it('refreshes one expired access token through the shared auth client', async () => {
    const test = await fixture(); test.backend.expireOnce = true; test.mount()
    await screen.findByRole('link', { name: item.title })
    expect(test.requests.filter(r => r.url === '/api/v1/auth/refresh')).toHaveLength(1)
  })
  it('locks duplicate read commands while the receipt is pending', async () => {
    const test = await fixture(`/notifications/${id}`); const gate = deferred<NotificationDetails>(); test.backend.gate = gate.promise; test.mount()
    const button = await screen.findByRole('button', { name: 'Đánh dấu đã đọc' })
    await userEvent.dblClick(button)
    expect(test.writes()).toHaveLength(1)
    expect(button).toBeDisabled()
    await act(async () => { gate.resolve({ ...item, readAt: '2026-10-30T02:00:00Z' }); await gate.promise })
    await screen.findByText('Đã đánh dấu thông báo là đã đọc.')
  })
  it('reconciles ACL denial from mark-read with both list and unread count', async () => {
    const test = await fixture(`/notifications/${id}`); test.mount(); await screen.findByRole('heading', { name: item.title })
    test.backend.rows = []; test.backend.writeStatus = 404
    await userEvent.click(screen.getByRole('button', { name: 'Đánh dấu đã đọc' }))
    await screen.findByRole('heading', { name: 'Thông báo không còn khả dụng' })
    await screen.findByRole('link', { name: 'Thông báo: 0 chưa đọc' })
    expect(test.writes()).toHaveLength(1)
    expect(screen.queryByRole('link', { name: 'Mở yêu cầu liên quan' })).not.toBeInTheDocument()
  })
  it('does not report a mismatched write receipt as success', async () => {
    const test = await fixture(`/notifications/${id}`); test.backend.receipt = { ...note, readAt: '2026-10-30T02:00:00Z' }; test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Đánh dấu đã đọc' }))
    await screen.findByText(/Chưa xác định được kết quả đánh dấu/)
    expect(screen.queryByText('Đã đánh dấu thông báo là đã đọc.')).not.toBeInTheDocument()
    expect(test.writes()).toHaveLength(1)
  })
  it('rechecks read state after a timeout without replaying PUT', async () => {
    const test = await fixture(`/notifications/${id}`); test.backend.failure = new ApiError('Timeout', 'timeout'); test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Đánh dấu đã đọc' }))
    await screen.findByText(/Chưa xác định được kết quả đánh dấu/)
    await userEvent.click(screen.getByRole('button', { name: 'Kiểm tra lại thông báo' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Đánh dấu đã đọc' })).toBeDisabled())
    expect(test.writes()).toHaveLength(1)
  })
  it('shows transient errors and supports REST retry without connecting SignalR', async () => {
    const test = await fixture(); test.backend.readStatus = 503; test.mount()
    await screen.findByText('Mã tra cứu: notification-trace')
    test.backend.readStatus = 200
    await userEvent.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Thử lại' }))
    await screen.findByRole('link', { name: item.title })
  })
})
