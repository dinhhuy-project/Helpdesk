import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { appRoutes } from '../../../app/router/routes'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { ApiError } from '../../../shared/api/api-error'
import { deferred, tokenPair, userA, userB } from '../../../test/auth-fixtures'
import type { AttachmentOptions, TicketAttachment } from '../types/ticket-attachment'
import type { TicketComment } from '../types/ticket-discussion'
import { ticketKeys } from '../hooks/use-tickets'
import { noSlaForPath } from '../../../test/sla-fixtures'

const id = 'a0000000-0000-0000-0000-000000000001'
const fileId = 'a0000000-0000-0000-0000-000000000002'
const commentId = 'a0000000-0000-0000-0000-000000000003'
const publicFile: TicketAttachment = { id: fileId, ticketId: id, commentId: null, uploadedByUserId: userA.id, fileName: 'evidence.txt', contentType: 'text/plain', sizeBytes: 4, isInternal: false, createdAt: '2026-10-29T00:00:00Z' }
const internalFile = { ...publicFile, id: 'a0000000-0000-0000-0000-000000000004', isInternal: true, fileName: 'private-file-marker.txt' }
const page = <T,>(items: T[], number = 1, totalCount = items.length, pageSize = 10) => ({ items, page: number, pageSize, totalCount })
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks() })

async function fixture(internal = false) {
  const profile = { ...(internal ? userB : userA), roles: ['SystemAdmin', 'SupportAgent'], permissions: ['ticket.read.all', 'ticket.comment.public', 'ticket.comment.internal'] }
  const requests: InternalAxiosRequestConfig[] = []
  const backend: {
    options: AttachmentOptions; files: TicketAttachment[]; comments: TicketComment[]; optionsGate?: Promise<void>
    postGate?: Promise<TicketAttachment>; postFailure?: ApiError; postStatus: number; postCode: string; accessStatus: number
    downloadGate?: Promise<ArrayBuffer>; downloadStatus: number; wrongVisibility: boolean; afterPost?: () => void
  } = {
    options: { access: { canReadInternal: internal, canUploadPublic: true, canUploadInternal: internal },
      policy: { enabled: true, maxFileSizeBytes: 1024, maxFilesPerVisibility: 2, maxTotalSizeBytesPerVisibility: 2048,
        fileTypes: [{ extension: '.txt', contentType: 'text/plain' }, { extension: '.pdf', contentType: 'application/pdf' }] } },
    files: [publicFile, internalFile], comments: [{ id: commentId, ticketId: id, authorId: profile.id, content: 'Bình luận đã lưu', isInternal: false, createdAt: publicFile.createdAt, updatedAt: null }],
    postStatus: 201, postCode: 'InvalidFile', accessStatus: 200, downloadStatus: 200, wrongVisibility: false,
  }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number, binary = false) => {
      if (status < 400) return
      const problem = { title: 'File bị từ chối.', code: backend.postCode, traceId: 'file-trace', errors: { File: ['File chưa hợp lệ.'] } }
      throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status,
        headers: new AxiosHeaders({ 'Retry-After': '30' }), data: binary ? new TextEncoder().encode(JSON.stringify(problem)).buffer : problem })
    }
    if (config.url === '/api/v1/auth/login' || config.url === '/api/v1/auth/refresh') return { ...response, data: tokenPair('files', profile) }
    if (config.url === '/api/v1/users/me') return { ...response, data: profile }
    if (config.url === '/api/v1/notifications/unread-count') return { ...response, data: { count: 0 } }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.endsWith('/sla')) return { ...response, data: noSlaForPath(config.url) }
    if (config.url?.endsWith('/attachments/options')) {
      await backend.optionsGate; fail(backend.accessStatus)
      const comment = backend.comments.find(item => item.id === config.params?.commentId)
      return { ...response, data: { ...backend.options, access: { ...backend.options.access,
        canUploadPublic: backend.options.access.canUploadPublic && (!comment || (!comment.isInternal && comment.authorId === profile.id)),
        canUploadInternal: backend.options.access.canUploadInternal && (!comment || (comment.isInternal && comment.authorId === profile.id)) } } }
    }
    if (config.url?.endsWith('/attachments') && config.method === 'post') {
      config.onUploadProgress?.({ loaded: 4, total: 4, bytes: 4, lengthComputable: true })
      if (backend.postFailure) throw backend.postFailure
      fail(backend.postStatus)
      if (backend.postGate) return { ...response, status: 201, data: await backend.postGate }
      const form: FormData = config.data
      const file = form.get('file') as File
      const targetComment = backend.comments.find(item => item.id === form.get('commentId'))
      const receipt = { ...publicFile, id: 'a0000000-0000-0000-0000-000000000005', fileName: file.name, sizeBytes: file.size,
        contentType: file.type, commentId: targetComment?.id ?? null, isInternal: targetComment?.isInternal ?? form.get('isInternal') === 'true', uploadedByUserId: profile.id }
      backend.files = [receipt, ...backend.files]; backend.afterPost?.()
      return { ...response, status: 201, data: receipt }
    }
    if (config.url?.endsWith('/attachments')) {
      fail(backend.accessStatus)
      const files = backend.wrongVisibility ? [internalFile] : backend.files.filter(file => file.isInternal === config.params.isInternal && (!config.params.commentId || file.commentId === config.params.commentId))
      return { ...response, data: page(config.params.page === 1 ? files : [], config.params.page, files.length + 10) }
    }
    if (config.url?.endsWith('/download')) {
      fail(backend.downloadStatus, true)
      return { ...response, data: backend.downloadGate ? await backend.downloadGate : new TextEncoder().encode('body').buffer }
    }
    if (config.url?.includes('/attachments/')) {
      return { ...response, data: backend.files.find(file => config.url?.endsWith(file.id)) }
    }
    if (config.url?.endsWith('/access')) { fail(backend.accessStatus); return { ...response, data: { canRead: true, canAssign: false, canUpdateStatus: false } } }
    if (config.url?.endsWith('/workflow')) return { ...response, data: { version: 81, status: 2, canAssign: false, assignmentIsValid: true, transitions: [], assignedTeamName: null, assignedAgentName: null } }
    if (config.url === `/api/v1/tickets/${id}`) return { ...response, data: { ...publicFile, title: 'Ticket có file', description: 'Mô tả yêu cầu', ticketNumber: 42, status: 2, priority: 2, requesterId: userA.id,
      categoryId: commentId, assignedTeamId: commentId, assignedAgentId: userB.id, version: 81, updatedAt: null, resolvedAt: null, closedAt: null } }
    if (config.url?.endsWith('/discussion-access')) return { ...response, data: { canReadInternal: internal, canCommentPublic: false, canCommentInternal: false } }
    if (config.url?.endsWith('/comments')) return { ...response, data: page(backend.comments.filter(comment => comment.isInternal === config.params.isInternal), 1, undefined, 20) }
    if (config.url?.endsWith('/activities')) return { ...response, data: page([], 1, 0, 20) }
    throw new Error('Unexpected fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: profile.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [`/tickets/${id}`] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { services, router, backend, requests, mount, posts: () => requests.filter(request => request.method === 'post' && request.url?.endsWith('/attachments')) }
}
async function chooseFile(name = 'upload.txt', type = 'text/plain') {
  await userEvent.upload(await screen.findByLabelText('Chọn file đính kèm'), new File(['body'], name, { type }), { applyAccept: false })
}
async function sendFile() { await userEvent.click(screen.getByRole('button', { name: 'Tải file lên' })) }

describe('Ticket attachments, visibility and multipart contract', () => {
  it('waits for server options; a requester never fetches or caches internal files despite role/internal metadata', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.optionsGate = gate.promise; test.mount()
    await screen.findByText('Đang kiểm tra quyền và giới hạn file…')
    expect(test.requests.some(request => request.url?.endsWith('/attachments'))).toBe(false)
    await act(async () => { gate.resolve(); await gate.promise })
    await screen.findByText(publicFile.fileName)
    expect(screen.queryByRole('button', { name: 'File nội bộ' })).not.toBeInTheDocument()
    expect(test.requests.filter(request => request.url?.endsWith('/attachments')).every(request => request.params?.isInternal === false)).toBe(true)
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(internalFile.fileName)
  })
  it('uploads exact multipart fields after an existing Ticket, reports sent vs committed and keeps File outside Query cache', async () => {
    const test = await fixture(); const gate = deferred<TicketAttachment>(); test.backend.postGate = gate.promise; test.mount(); await chooseFile()
    await sendFile(); await screen.findByText('100% · Đã gửi file, đang xác nhận lưu…')
    expect(screen.queryByText('Đã tải file lên.')).not.toBeInTheDocument()
    const request = test.posts()[0]!
    const form: FormData = request.data
    expect([...form.keys()].sort()).toEqual(['file', 'isInternal'])
    expect(form.get('isInternal')).toBe('false')
    expect(request.headers.get('Authorization')).toBe('Bearer access-files')
    expect(request.headers.getContentType()).not.toBe('application/json')
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
    await act(async () => { gate.resolve({ ...publicFile, fileName: 'upload.txt' }); await gate.promise })
    await screen.findByText('Đã tải file lên.')
    expect(test.posts()).toHaveLength(1)
  })
  it.each([['empty.txt', 'text/plain', 0], ['large.txt', 'text/plain', 1025], ['evil.html', 'text/html', 4], ['wrong.txt', 'application/pdf', 4], ['../bad.txt', 'text/plain', 4]] as const)(
    'validates configured size/type/name for %s before upload', async (name, type, size) => {
      const test = await fixture(); test.mount()
      const file = new File([size ? 'body' : ''], name, { type }); Object.defineProperty(file, 'size', { value: size })
      await userEvent.upload(await screen.findByLabelText('Chọn file đính kèm'), file, { applyAccept: false })
      await sendFile(); expect(await screen.findByRole('alert')).toBeInTheDocument(); expect(test.posts()).toHaveLength(0)
    })
  it('uses server format when the OS supplies no MIME type, and never sends identity/storage fields', async () => {
    const test = await fixture(); test.mount(); await chooseFile('upload.txt', ''); await sendFile(); await screen.findByText('Đã tải file lên.')
    const form: FormData = test.posts()[0]!.data
    expect((form.get('file') as File).type).toBe('text/plain')
    expect([...form.keys()]).toEqual(['file', 'isInternal'])
  })
  it('loads internal files only on explicit selection and purges them when capability is removed', async () => {
    const test = await fixture(true); test.mount(); await screen.findByText(publicFile.fileName)
    expect(screen.queryByText(internalFile.fileName)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'File nội bộ' })); await screen.findByText(internalFile.fileName)
    await chooseFile('internal-draft.txt')
    test.backend.options = { ...test.backend.options, access: { canReadInternal: false, canUploadPublic: true, canUploadInternal: false } }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại quyền file' }))
    await screen.findByText(publicFile.fileName)
    expect(screen.queryByText(/internal-draft.txt/)).not.toBeInTheDocument()
    await waitFor(() => expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(internalFile.fileName))
    expect(screen.queryByRole('button', { name: 'File nội bộ' })).not.toBeInTheDocument()
  })
  it('paginates on the server and never keeps prior rows while a new page loads', async () => {
    const test = await fixture(); test.mount(); await screen.findByText(publicFile.fileName)
    const section = screen.getByRole('region', { name: 'File của yêu cầu' })
    await userEvent.click(within(section).getByRole('button', { name: 'Trang sau' }))
    await within(section).findByText('Chưa có file trong phạm vi này')
    expect(test.requests.filter(request => request.url?.endsWith('/attachments')).at(-1)?.params).toMatchObject({ page: 2, pageSize: 10, isInternal: false })
    expect(within(section).queryByText(publicFile.fileName)).not.toBeInTheDocument()
  })
  it('inherits comment visibility, only sends commentId and file, and leaves comment text untouched', async () => {
    const test = await fixture(); test.mount(); await screen.findByText('Bình luận đã lưu')
    await userEvent.click(screen.getByRole('button', { name: 'File của bình luận' }))
    const dialog = screen.getByRole('dialog', { name: 'File đính kèm bình luận' })
    await userEvent.upload(await within(dialog).findByLabelText('Chọn file đính kèm'), new File(['body'], 'comment.txt', { type: 'text/plain' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Tải file lên' })); await screen.findByText('Đã tải file lên.')
    const form: FormData = test.posts()[0]!.data
    expect([...form.keys()].sort()).toEqual(['commentId', 'file']); expect(form.get('commentId')).toBe(commentId)
    expect(test.requests.some(request => request.method === 'post' && request.url?.endsWith('/comments'))).toBe(false)
    expect(screen.getByText('Bình luận đã lưu')).toBeInTheDocument()
  })
  it('offers read/download only on someone else’s comment', async () => {
    const test = await fixture(); test.backend.comments = [{ ...test.backend.comments[0]!, authorId: userB.id }]; test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'File của bình luận' }))
    const dialog = screen.getByRole('dialog', { name: 'File đính kèm bình luận' })
    await within(dialog).findByText('Bạn hiện không được đính kèm file tại đây.')
    expect(within(dialog).queryByLabelText('Chọn file đính kèm')).not.toBeInTheDocument()
  })
  it('rejects an unexpected internal response before Query stores any private metadata', async () => {
    const test = await fixture(); test.backend.wrongVisibility = true; test.mount()
    await screen.findByText('Dữ liệu file không đúng phạm vi yêu cầu.')
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(internalFile.fileName)
  })
  it('whitelists metadata fields so unexpected storage coordinates never reach cache or DOM', async () => {
    const test = await fixture()
    const contaminated = { ...publicFile, objectKey: 'storage-coordinate-marker', bucket: 'bucket-coordinate-marker', url: 'https://storage.example.test/private' }
    test.backend.files = [contaminated]; test.mount(); await screen.findByText(publicFile.fileName)
    const cache = JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))
    expect(cache).not.toContain('coordinate-marker'); expect(cache).not.toContain('storage.example.test')
    expect(document.body.textContent).not.toContain('coordinate-marker')
    expect(document.querySelector('a[href^="https://storage.example.test"]')).toBeNull()
  })
  it.each(['timeout', 'network'] as const)('keeps selected file and blocks automatic or manual replay after ambiguous %s', async kind => {
    const test = await fixture(); test.backend.postFailure = new ApiError('Kết nối lỗi.', kind); test.mount(); await chooseFile(); await sendFile()
    await screen.findByText(/Chưa xác định được kết quả tải file/)
    expect(screen.getByText('upload.txt')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Tải file lên' })).toBeDisabled()
    await sendFile(); expect(test.posts()).toHaveLength(1)
  })
  it.each([400, 409, 413, 415, 429, 503])('handles upload HTTP %i and retains file without automatic retry', async status => {
    const test = await fixture(); test.backend.postStatus = status; test.backend.postCode = status === 409 ? 'AttachmentQuotaExceeded' : 'InvalidFile'; test.mount(); await chooseFile(); await sendFile()
    await screen.findByText('Mã tra cứu: file-trace')
    expect(screen.getByText('upload.txt')).toBeInTheDocument(); expect(test.posts()).toHaveLength(1)
    if (status === 429) expect(screen.getByText('Vui lòng chờ 30 giây trước khi thử lại.')).toBeInTheDocument()
    if (status === 503 || status === 429) expect(screen.getByRole('button', { name: 'Tải file lên' })).toBeDisabled()
  })
  it('keeps committed upload successful if the following list read loses access', async () => {
    const test = await fixture(); test.mount(); await chooseFile(); test.backend.afterPost = () => { test.backend.accessStatus = 404 }
    await sendFile(); await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(screen.getByText('Đã tải file lên.')).toBeInTheDocument()
    expect(test.posts()).toHaveLength(1)
    expect(screen.queryByText(publicFile.fileName)).not.toBeInTheDocument()
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
  })
  it.each([403, 404])('rechecks resource access and clears old metadata on upload HTTP %i', async status => {
    const test = await fixture(); test.mount(); await chooseFile()
    test.backend.postStatus = status; test.backend.accessStatus = status
    await sendFile()
    await screen.findByRole('heading', { name: status === 403 ? 'Không có quyền truy cập' : 'Không tìm thấy yêu cầu' })
    await waitFor(() => expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain(publicFile.fileName))
    expect(test.posts()).toHaveLength(1)
  })
  it('fetches fresh metadata and Bearer binary data, saves Blob then revokes the object URL', async () => {
    const test = await fixture(); const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test-download')
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    test.mount(); await userEvent.click(await screen.findByRole('button', { name: `Tải xuống ${publicFile.fileName}` }))
    await waitFor(() => expect(create).toHaveBeenCalledTimes(1))
    expect(create.mock.calls[0]?.[0]).toBeInstanceOf(Blob); expect(revoke).toHaveBeenCalledWith('blob:test-download'); expect(click).toHaveBeenCalledOnce()
    const download = test.requests.find(request => request.url?.endsWith('/download'))!
    expect(download.responseType).toBe('arraybuffer'); expect(download.headers.get('Authorization')).toBe('Bearer access-files')
    expect(test.requests.some(request => request.url === `/api/v1/tickets/${id}/attachments/${fileId}`)).toBe(true)
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain('blob:test-download')
    expect(document.querySelector('a[download]')).toBeNull()
  })
  it('decodes binary ProblemDetails rather than downloading an error page', async () => {
    const test = await fixture(); test.backend.downloadStatus = 429
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:never')
    test.mount(); await userEvent.click(await screen.findByRole('button', { name: `Tải xuống ${publicFile.fileName}` }))
    await screen.findByText('Mã tra cứu: file-trace')
    expect(create).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: `Tải xuống ${publicFile.fileName}` })).toBeDisabled()
  })
  it('revokes the object URL and removes the anchor even when starting the download fails', async () => {
    const test = await fixture(); vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:failed-download')
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => { throw new Error('Browser download failed') })
    test.mount(); await userEvent.click(await screen.findByRole('button', { name: `Tải xuống ${publicFile.fileName}` }))
    await waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:failed-download'))
    expect(document.querySelector('a[download]')).toBeNull()
  })
  it('discards late binary download after capability revalidation removes internal access', async () => {
    const test = await fixture(true); const gate = deferred<ArrayBuffer>(); test.backend.downloadGate = gate.promise
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:never')
    test.mount(); await userEvent.click(await screen.findByRole('button', { name: 'File nội bộ' })); await userEvent.click(await screen.findByRole('button', { name: `Tải xuống ${internalFile.fileName}` }))
    await waitFor(() => expect(test.requests.some(request => request.url?.endsWith('/download'))).toBe(true))
    test.backend.options = { ...test.backend.options, access: { canReadInternal: false, canUploadPublic: true, canUploadInternal: false } }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại quyền file' })); await screen.findByText(publicFile.fileName)
    await act(async () => { gate.resolve(new ArrayBuffer(4)); await gate.promise })
    expect(create).not.toHaveBeenCalled()
  })
  it('does not resurrect an upload or File after logout while a receipt is in flight', async () => {
    const test = await fixture(); const gate = deferred<TicketAttachment>(); test.backend.postGate = gate.promise; test.mount(); await chooseFile(); await sendFile()
    await waitFor(() => expect(test.posts()).toHaveLength(1))
    await act(async () => { await test.services.session.logout() })
    await act(async () => { gate.resolve(publicFile); await gate.promise })
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
    expect(screen.queryByText('Đã tải file lên.')).not.toBeInTheDocument()
    expect(screen.queryByText('upload.txt')).not.toBeInTheDocument()
  })
  it('holds uploads if storage is disabled or the server revokes current write capabilities', async () => {
    const test = await fixture(); test.backend.options = { ...test.backend.options, policy: { ...test.backend.options.policy, enabled: false }, access: { canReadInternal: false, canUploadPublic: false, canUploadInternal: false } }
    test.mount(); await screen.findByText('Dịch vụ lưu file chưa được bật.')
    expect(screen.queryByLabelText('Chọn file đính kèm')).not.toBeInTheDocument()
    expect(test.posts()).toHaveLength(0)
  })
  it('revalidating Ticket access never auto uploads a selected file', async () => {
    const test = await fixture(); test.mount(); await chooseFile()
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: ticketKeys.resource(test.services.session.store.getState().scope, id) }) })
    expect(await screen.findByText('upload.txt')).toBeInTheDocument(); expect(test.posts()).toHaveLength(0)
  })
})
