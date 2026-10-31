import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { ApiError } from '../../../shared/api/api-error'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { Permission } from '../../auth/lib/permissions'
import { tokenPair, userA } from '../../../test/auth-fixtures'
import { publicCommunityApi, type ManagedEntry, type PublicEntry } from '../api/community-api'
import { CommunityPublicDetail, CommunityPublicList } from './public-community-pages'
import { CommunityManageDetail } from './manage-community-pages'
import { publicCommunityKey } from '../hooks/use-community'

const id = '72000000-0000-0000-0000-000000000001'
const published: PublicEntry = { id, kind: 'Knowledge', title: 'Hướng dẫn VPN đã duyệt', summary: 'Hướng dẫn công khai', content: '<img src=x onerror=alert(1)> Nội dung văn bản an toàn', topic: 'Mạng',
  status: null, revision: 1, publishedAt: '2026-10-30T00:00:00Z', sourceUrl: `/knowledge/${id}`, affectedCount: 0, helpfulCount: 2, solvedCount: 1 }
const draft: ManagedEntry = { id, kind: 'Knowledge', title: 'Bài nháp VPN', summary: 'Tóm tắt bản soạn', content: 'Nội dung đang biên soạn', topic: 'Mạng', status: null,
  isPublished: false, publicRevision: 0, publishedAt: null, version: 41, sourceTicketId: null, updatedAt: published.publishedAt }
const dispose: (() => void)[] = []
afterEach(() => { cleanup(); for (const fn of dispose.splice(0)) fn(); vi.restoreAllMocks() })
async function open(path = `/knowledge/${id}`, grants?: string[]) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend = { row: structuredClone(draft), conflict: false }
  const profile = { ...userA, permissions: grants ?? [] }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { status: 200, statusText: 'OK', headers: new AxiosHeaders(), config, data: undefined as unknown }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('community-test', profile) }
    if (config.url === '/api/v1/users/me') return { ...response, data: profile }
    if (config.url?.endsWith('/participation')) return { ...response, data: { affected: false, helpful: null, solved: false } }
    if (config.method === 'get') return { ...response, data: backend.row }
    if (backend.conflict) throw new AxiosError('Conflict', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status: 409, data: { code: 'VersionConflict', traceId: 'community-trace' } })
    const body = JSON.parse(config.data) as Partial<ManagedEntry>
    if (config.url?.endsWith('/publish')) backend.row = { ...backend.row, isPublished: true, publicRevision: 1, version: backend.row.version + 1 }
    else backend.row = { ...backend.row, ...body, ...(typeof body.title === 'string' ? { title: body.title.trim() } : {}), version: backend.row.version + 1 }
    return { ...response, data: backend.row }
  }
  const services = createAppServices({ baseURL: '/', adapter })
  if (grants) await services.session.login({ email: userA.email, password: 'unit-test-only-password' })
  const router = createMemoryRouter([
    { path: '/knowledge', element: <CommunityPublicList kind="knowledge" /> },
    { path: '/knowledge/:id', element: <CommunityPublicDetail kind="knowledge" /> },
    { path: '/admin/knowledge/:id', element: <CommunityManageDetail kind="knowledge" /> },
  ], { initialEntries: [path] })
  dispose.push(() => { router.dispose(); services.queryClient.clear() })
  render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { ...services, requests, backend, router }
}

describe('public community portal and controlled publishing', () => {
  it('allows anonymous detail lookup and renders content as text without executing HTML', async () => {
    vi.spyOn(publicCommunityApi, 'get').mockResolvedValue(published)
    const test = await open()
    expect(await screen.findByRole('heading', { name: published.title })).toBeInTheDocument()
    expect(screen.getByText(published.content!)).toBeInTheDocument()
    expect(document.querySelector('img')).toBeNull()
    expect(screen.getByRole('link', { name: 'Đăng nhập' })).toHaveAttribute('href', expect.stringContaining('returnTo='))
    expect(test.requests).toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Hướng dẫn hữu ích' })).not.toBeInTheDocument()
  })
  it('removes previously visible content after a public withdrawal invalidation', async () => {
    const get = vi.spyOn(publicCommunityApi, 'get').mockResolvedValue(published)
    const test = await open()
    await screen.findByRole('heading', { name: published.title })
    get.mockRejectedValue(new ApiError('Không còn công khai', 'http', 404))
    await act(async () => { await test.queryClient.resetQueries({ queryKey: publicCommunityKey }) })
    expect(await screen.findByRole('alert')).toHaveTextContent('Không còn công khai')
    expect(screen.queryByText(published.content!)).not.toBeInTheDocument()
  })
  it('keeps search and page in the URL and requests server results', async () => {
    const list = vi.spyOn(publicCommunityApi, 'list').mockResolvedValue({ items: [published], page: 1, pageSize: 12, totalCount: 24 })
    const test = await open('/knowledge')
    await screen.findByRole('link', { name: published.title })
    await userEvent.type(screen.getByLabelText('Tìm kiếm hướng dẫn hoặc sự cố'), 'VPN')
    await userEvent.click(screen.getByRole('button', { name: 'Tra cứu' }))
    await waitFor(() => expect(test.router.state.location.search).toContain('search=VPN'))
    await userEvent.click(screen.getByRole('button', { name: 'Trang sau' }))
    await waitFor(() => expect(list).toHaveBeenLastCalledWith('knowledge', expect.objectContaining({ search: 'VPN', page: 2 }), expect.any(AbortSignal)))
  })
  it('requires explicit review before publish and sends the server version', async () => {
    const test = await open(`/admin/knowledge/${id}`, [Permission.KnowledgeManage, Permission.KnowledgePublish])
    await screen.findByDisplayValue(draft.title)
    expect(screen.getByRole('button', { name: 'Xuất bản công khai' })).toBeDisabled()
    await userEvent.click(screen.getByLabelText(/Tôi đã kiểm tra nội dung/))
    await userEvent.click(screen.getByRole('button', { name: 'Xuất bản công khai' }))
    await screen.findByText('Đã xuất bản nội dung công khai.')
    const request = test.requests.find(x => x.url?.endsWith('/publish'))
    expect(JSON.parse(request!.data)).toEqual({ version: 41, reviewedForPublic: true, message: null })
  })
  it('saving a changed draft never auto-publishes it', async () => {
    const test = await open(`/admin/knowledge/${id}`, [Permission.KnowledgeManage, Permission.KnowledgePublish])
    await screen.findByDisplayValue(draft.title)
    await userEvent.type(screen.getByRole('textbox', { name: 'Tiêu đề' }), ' cập nhật ')
    await userEvent.click(screen.getByRole('button', { name: 'Lưu bản soạn' }))
    await screen.findByText('Đã lưu bản soạn. Nội dung chỉ công khai sau khi xuất bản.')
    expect(test.requests.some(x => x.url?.endsWith('/publish'))).toBe(false)
    expect(test.backend.row.isPublished).toBe(false)
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('Bài nháp VPN cập nhật')
    await userEvent.click(screen.getByLabelText(/Tôi đã kiểm tra nội dung/))
    expect(screen.getByRole('button', { name: 'Xuất bản công khai' })).toBeEnabled()
  })
  it('preserves typed content on conflict and waits for explicit version reload', async () => {
    const test = await open(`/admin/knowledge/${id}`, [Permission.KnowledgeManage])
    await screen.findByDisplayValue(draft.title)
    test.backend.conflict = true
    await userEvent.clear(screen.getByRole('textbox', { name: 'Tiêu đề' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Tiêu đề' }), 'Nội dung tôi đang nhập')
    await userEvent.click(screen.getByRole('button', { name: 'Lưu bản soạn' }))
    await screen.findByText('Mã tra cứu: community-trace')
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('Nội dung tôi đang nhập')
    test.backend.row.version = 77; test.backend.conflict = false
    await userEvent.click(screen.getByRole('button', { name: 'Giữ nội dung nhập và dùng phiên bản mới nhất' }))
    expect(screen.getByRole('textbox', { name: 'Tiêu đề' })).toHaveValue('Nội dung tôi đang nhập')
    await userEvent.click(screen.getByRole('button', { name: 'Lưu bản soạn' }))
    await screen.findByText('Đã lưu bản soạn. Nội dung chỉ công khai sau khi xuất bản.')
    const put = test.requests.filter(x => x.method === 'put').at(-1)
    expect(JSON.parse(put!.data).version).toBe(77)
    expect(screen.queryByRole('button', { name: 'Xuất bản công khai' })).not.toBeInTheDocument()
  })
})
