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
import { deferred, tokenPair, userA, userB } from '../../../test/auth-fixtures'
import type { PagedResult } from '../../../shared/api/pagination'
import type { TicketComment, TicketDiscussionAccess } from '../types/ticket-discussion'
import { noAttachmentUpload } from '../../../test/attachment-fixtures'
import { noSlaForPath } from '../../../test/sla-fixtures'

const id = '40000000-0000-0000-0000-000000000001'
const noteId = '40000000-0000-0000-0000-000000000002'
const publicId = '40000000-0000-0000-0000-000000000003'
const publicComment: TicketComment = { id: publicId, ticketId: id, authorId: userB.id, content: 'Phản hồi công khai', isInternal: false, createdAt: '2026-10-29T00:00:00Z', updatedAt: null }
const note: TicketComment = { ...publicComment, id: noteId, content: 'private-note-marker', isInternal: true }
const page = (items: TicketComment[], number = 1, totalCount = items.length): PagedResult<TicketComment> => ({ items, page: number, pageSize: 20, totalCount })
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()); vi.useRealTimers() })

async function fixture(internal = false, search = '') {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: {
    access: TicketDiscussionAccess; capsGate?: Promise<TicketDiscussionAccess>; comments: PagedResult<TicketComment>
    postStatus: number; postCode?: string; postFailure?: unknown; postGate?: Promise<TicketComment>; readStatus: number
  } = { access: { canReadInternal: internal, canCommentPublic: true, canCommentInternal: internal }, comments: page([publicComment]), postStatus: 201, readStatus: 200 }
  // Role/permission metadata deliberately includes internal on requester, while resource capability excludes it.
  const user = { ...(internal ? userB : userA), roles: ['SystemAdmin', 'SupportAgent'], permissions: ['ticket.read.all', 'ticket.comment.internal', 'ticket.comment.public'] }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status,
        headers: new AxiosHeaders({ 'Retry-After': '2' }), data: { title: 'Không thể gửi.', traceId: 'comment-trace', code: backend.postCode, errors: { Content: ['Nội dung chưa hợp lệ.'] } } })
    }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('discussion', user) }
    if (config.url === '/api/v1/users/me') return { ...response, data: user }
    if (config.url === '/api/v1/notifications/unread-count') return { ...response, data: { count: 0 } }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.endsWith('/sla')) return { ...response, data: noSlaForPath(config.url) }
    if (config.url?.endsWith('/attachments/options')) return { ...response, data: noAttachmentUpload }
    if (config.url?.endsWith('/attachments')) return { ...response, data: page([]) }
    if (config.url?.endsWith('/workflow')) return { ...response, data: { version: 123, status: 2, canAssign: false, assignmentIsValid: true, transitions: [], assignedTeamName: null, assignedAgentName: null } }
    if (config.url?.endsWith('/discussion-access')) return { ...response, data: backend.capsGate ? await backend.capsGate : backend.access }
    if (config.url?.endsWith('/access')) { fail(backend.readStatus); return { ...response, data: { canRead: true, canAssign: true, canUpdateStatus: true } } }
    if (config.url === `/api/v1/tickets/${id}`) return { ...response, data: { id, ticketNumber: 56, title: 'Ticket thảo luận', description: 'Mô tả Ticket', status: 2, priority: 2,
      requesterId: userA.id, categoryId: noteId, assignedTeamId: noteId, assignedAgentId: userB.id, createdAt: publicComment.createdAt, updatedAt: null, resolvedAt: null, closedAt: null, version: 123 } }
    if (config.url?.endsWith('/comments') && config.method === 'post') {
      if (backend.postFailure) throw backend.postFailure
      fail(backend.postStatus)
      const value: { content: string; isInternal: boolean } = JSON.parse(config.data)
      const result: TicketComment = { ...publicComment, id: '40000000-0000-0000-0000-000000000004', authorId: user.id, ...value }
      if (!backend.postGate) backend.comments = page([result, publicComment])
      return { ...response, status: backend.postStatus, data: backend.postGate ? await backend.postGate : result }
    }
    if (config.url?.endsWith('/comments')) return { ...response, data: config.params.isInternal ? page([note]) : backend.comments }
    if (config.url?.endsWith('/activities')) return { ...response, data: { items: [], page: 1, pageSize: 20, totalCount: 0 } }
    throw new Error('Unexpected fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: user.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [`/tickets/${id}${search}`] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}>
    <RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { backend, services, router, requests, mount, posts: () => requests.filter(config => config.method === 'post' && config.url?.endsWith('/comments')) }
}
const textbox = () => screen.getByRole('textbox', { name: 'Nội dung bình luận' })

describe('discussion ACL, visibility and cache isolation', () => {
  it('waits for discussion capabilities before fetching comments or activities', async () => {
    const test = await fixture()
    const gate = deferred<TicketDiscussionAccess>(); test.backend.capsGate = gate.promise
    test.mount()
    await screen.findByText('Đang kiểm tra quyền thảo luận…')
    expect(test.requests.some(config => /\/(comments|activities)$/.test(config.url ?? ''))).toBe(false)
    await act(async () => { gate.resolve(test.backend.access); await gate.promise })
    await screen.findByText(publicComment.content)
  })
  it('keeps requester requests and cache public even with admin/internal grants', async () => {
    const test = await fixture(); test.mount()
    await screen.findByText(publicComment.content)
    expect(screen.queryByRole('button', { name: 'Nội bộ' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ghi chú nội bộ' })).not.toBeInTheDocument()
    expect(test.requests.filter(config => /\/(comments|activities)$/.test(config.url ?? '')).every(config => config.params.isInternal === false)).toBe(true)
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(note.content)
  })
  it('blocks a requester internal deep link without issuing an internal read', async () => {
    const test = await fixture(false, '?comments=internal&commentPage=2'); test.mount()
    await screen.findByText('Bạn không có quyền đọc ghi chú nội bộ của yêu cầu này.')
    expect(test.requests.some(config => config.url?.endsWith('/comments'))).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Xem bình luận công khai' }))
    await screen.findByText(publicComment.content)
    expect(test.router.state.location.search).toBe('')
  })
  it('pages comments independently from activity and preserves URL visibility', async () => {
    const test = await fixture(false, '?commentPage=3&activityPage=2'); test.backend.comments = page([publicComment], 3, 45); test.mount()
    await screen.findByText(publicComment.content)
    const region = screen.getByRole('region', { name: 'Bình luận công khai' })
    expect(within(region).getByText('Trang 3 / 3 · 45 kết quả')).toBeInTheDocument()
    await userEvent.click(within(region).getByRole('button', { name: 'Trang trước' }))
    await waitFor(() => expect(test.router.state.location.search).toContain('commentPage=2'))
    expect(test.router.state.location.search).toContain('activityPage=2')
  })
  it('fetches internal notes only after an authorized explicit selection', async () => {
    const test = await fixture(true); test.mount(); await screen.findByText(publicComment.content)
    expect(screen.queryByText(note.content)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Nội bộ' }))
    await screen.findByText(note.content)
    expect(test.router.state.location.search).toContain('comments=internal')
    expect(test.requests.filter(config => config.url?.endsWith('/comments')).at(-1)?.params.isInternal).toBe(true)
  })
  it('purges private cache and draft when current resource capability is revoked', async () => {
    const test = await fixture(true, '?comments=internal'); test.mount(); await screen.findByText(note.content)
    await userEvent.click(screen.getByRole('button', { name: 'Ghi chú nội bộ' }))
    await userEvent.type(textbox(), 'private draft')
    test.backend.access = { canReadInternal: false, canCommentInternal: false, canCommentPublic: true }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại thảo luận' }))
    await screen.findByText('Bạn không có quyền đọc ghi chú nội bộ của yêu cầu này.')
    await waitFor(() => expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(note.content))
    expect(textbox()).toHaveValue('')
    expect(screen.queryByText(note.content)).not.toBeInTheDocument()
  })
  it('rejects a wrong-visibility server response before storing it in query data', async () => {
    const test = await fixture(); test.backend.comments = page([note]); test.mount()
    await screen.findByText('Dữ liệu thảo luận không đúng phạm vi yêu cầu.')
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(note.content)
    expect(screen.queryByText(note.content)).not.toBeInTheDocument()
  })
  it('retains only append behavior and respects read-only/terminal resource capabilities', async () => {
    const test = await fixture(true); test.backend.access.canCommentPublic = false; test.backend.access.canCommentInternal = false; test.mount()
    await screen.findByText(publicComment.content)
    expect(screen.queryByRole('textbox', { name: 'Nội dung bình luận' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Sửa|Xóa|Đổi visibility/ })).not.toBeInTheDocument()
    expect(screen.getByText('Bạn có thể đọc lịch sử nhưng hiện không được gửi bình luận mới.')).toBeInTheDocument()
  })
})
describe('append command and post-commit refresh', () => {
  it('posts the exact public DTO, refreshes comments/activity and clears draft only after success', async () => {
    const test = await fixture(); test.mount(); await screen.findByText(publicComment.content)
    await userEvent.type(textbox(), '  Phản hồi mới  ')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi bình luận' }))
    await screen.findByText('Phản hồi mới')
    expect(test.posts()).toHaveLength(1)
    expect(JSON.parse(test.posts()[0]?.data)).toEqual({ content: 'Phản hồi mới', isInternal: false })
    expect(test.posts()[0]?.headers.get('Authorization')).toBe('Bearer access-discussion')
    expect(textbox()).toHaveValue('')
    expect(test.requests.filter(config => config.url?.endsWith('/activities')).length).toBeGreaterThan(1)
  })
  it('never converts an internal draft into a public comment when switching mode', async () => {
    const test = await fixture(true); test.mount(); await screen.findByText(publicComment.content)
    await userEvent.click(screen.getByRole('button', { name: 'Ghi chú nội bộ' }))
    await userEvent.type(textbox(), 'private draft')
    await userEvent.click(screen.getByRole('button', { name: 'Bình luận công khai' }))
    expect(textbox()).toHaveValue('')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi bình luận' }))
    expect(test.posts()).toHaveLength(0)
  })
  it('sends internal independently from public permission', async () => {
    const test = await fixture(true); test.backend.access.canCommentPublic = false; test.mount(); await screen.findByText(publicComment.content)
    await userEvent.click(screen.getByRole('button', { name: 'Ghi chú nội bộ' }))
    await userEvent.type(textbox(), 'Ghi chú riêng')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi ghi chú nội bộ' }))
    await screen.findByText('Đã gửi ghi chú nội bộ.')
    expect(JSON.parse(test.posts()[0]?.data)).toEqual({ content: 'Ghi chú riêng', isInternal: true })
    expect(test.router.state.location.search).toContain('comments=internal')
  })
  it('keeps a public draft and in-flight lock through resource revalidation', async () => {
    const test = await fixture(); test.mount(); await screen.findByText(publicComment.content)
    await userEvent.type(textbox(), 'Nội dung đang soạn')
    await act(async () => { await test.services.queryClient.invalidateQueries({ predicate: query => query.queryKey.at(-1) === 'access' }) })
    await screen.findByText(publicComment.content)
    expect(textbox()).toHaveValue('Nội dung đang soạn')
    const gate = deferred<TicketComment>(); test.backend.postGate = gate.promise
    const form = screen.getByRole('form', { name: 'Gửi bình luận' })
    act(() => { fireEvent.submit(form); fireEvent.submit(form) })
    await waitFor(() => expect(test.posts()).toHaveLength(1))
    await act(async () => { await test.services.queryClient.invalidateQueries({ predicate: query => query.queryKey.at(-1) === 'access' }) })
    await screen.findByText(publicComment.content)
    expect(screen.getByRole('button', { name: 'Gửi bình luận' })).toBeDisabled()
    await act(async () => { gate.resolve({ ...publicComment, authorId: userA.id, content: 'Nội dung đang soạn' }); await gate.promise })
    await screen.findByText('Đã gửi bình luận công khai.')
  })
  it.each(['timeout', 'network', 'server'])('does not automatically replay an uncertain %s append', async kind => {
    const test = await fixture(); test.mount(); await screen.findByText(publicComment.content)
    if (kind === 'server') test.backend.postStatus = 503
    else test.backend.postFailure = new AxiosError('Transport', kind === 'timeout' ? 'ECONNABORTED' : 'ERR_NETWORK')
    await userEvent.type(textbox(), 'Giữ bản nháp')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi bình luận' }))
    await screen.findByRole('heading', { name: 'Chưa xác định được kết quả gửi' })
    expect(textbox()).toHaveValue('Giữ bản nháp')
    expect(screen.getByRole('button', { name: 'Gửi bình luận' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại thảo luận' }))
    await screen.findByText(publicComment.content)
    expect(textbox()).toHaveValue('Giữ bản nháp')
    expect(screen.getByRole('button', { name: 'Gửi bình luận' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Tôi đã kiểm tra, cho phép gửi lại' }))
    expect(test.posts()).toHaveLength(1)
  })
  it('maps validation fields and renders comment HTML as plain text', async () => {
    const test = await fixture(); test.backend.comments = page([{ ...publicComment, content: '<img src=x onerror=alert(1)>' }]); test.mount()
    await screen.findByText('<img src=x onerror=alert(1)>')
    expect(document.querySelector('img[src="x"]')).not.toBeInTheDocument()
    test.backend.postStatus = 400
    await userEvent.type(textbox(), 'Nội dung')
    await userEvent.click(screen.getByRole('button', { name: 'Gửi bình luận' }))
    await screen.findByText('Nội dung chưa hợp lệ.')
    expect(textbox()).toHaveValue('Nội dung')
    expect(screen.getByText(/comment-trace/)).toBeInTheDocument()
  })
  it('honors Retry-After without automatically resending a comment', async () => {
    const test = await fixture(); test.mount(); await screen.findByText(publicComment.content)
    test.backend.postStatus = 429
    await userEvent.type(textbox(), 'Chờ gửi')
    vi.useFakeTimers()
    await act(async () => { fireEvent.submit(screen.getByRole('form', { name: 'Gửi bình luận' })); await vi.advanceTimersByTimeAsync(1) })
    expect(screen.getByText('Vui lòng chờ 2 giây trước khi thử lại.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Gửi bình luận' })).toBeDisabled()
    await act(async () => { await vi.advanceTimersByTimeAsync(2_000) })
    expect(screen.getByRole('button', { name: 'Gửi bình luận' })).toBeEnabled()
    expect(test.posts()).toHaveLength(1)
  })
  it('discards a late internal append response after logout and clears private query data', async () => {
    const test = await fixture(true, '?comments=internal'); test.mount(); await screen.findByText(note.content)
    await userEvent.click(screen.getByRole('button', { name: 'Ghi chú nội bộ' }))
    await userEvent.type(textbox(), 'private pending')
    const gate = deferred<TicketComment>(); test.backend.postGate = gate.promise
    await userEvent.click(screen.getByRole('button', { name: 'Gửi ghi chú nội bộ' }))
    await waitFor(() => expect(test.posts()).toHaveLength(1))
    await act(async () => { await test.services.session.logout() })
    await act(async () => { gate.resolve({ ...note, authorId: userB.id, content: 'private pending' }); await gate.promise })
    expect(test.posts()[0]?.signal?.aborted).toBe(true)
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
    expect(screen.queryByText(note.content)).not.toBeInTheDocument()
    expect(screen.queryByText('Đã gửi ghi chú nội bộ.')).not.toBeInTheDocument()
  })
})

