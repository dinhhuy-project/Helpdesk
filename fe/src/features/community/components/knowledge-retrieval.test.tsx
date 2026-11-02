import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { KnowledgeRetrieval } from './knowledge-retrieval'
import { publicCommunityApi, type KnowledgePassage, type KnowledgeRetrievalResult } from '../api/community-api'
import { publicCommunityKey } from '../hooks/use-community'
import { ApiError } from '../../../shared/api/api-error'

const id = '72000000-0000-0000-0000-000000000001'
const passage: KnowledgePassage = {
  entryId: id, revision: 3, title: 'Đổi mật khẩu an toàn', topic: 'Tài khoản', section: 'Các bước thực hiện', step: 2,
  text: '2. Chọn đổi mật khẩu, nhập mật khẩu hiện tại và mật khẩu mới.', sourceUrl: `/knowledge/${id}`,
  applicability: 'Tài khoản đang active và đã đăng nhập.', sourceName: 'Hướng dẫn đã được rà soát', referenceUrl: null,
  sourceUpdatedOn: '2026-10-30', publishedAt: '2026-10-31T00:00:00Z',
}
const result: KnowledgeRetrievalResult = { query: 'doi mk', method: 'lexical-v1', candidateLimitReached: false, items: [passage] }
const disposers: (() => void)[] = []
afterEach(() => { cleanup(); disposers.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks() })
function open(url = '/knowledge') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([{ path: '/knowledge', element: <KnowledgeRetrieval /> }], { initialEntries: [url] })
  disposers.push(() => { router.dispose(); client.clear() })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return { client, router }
}

describe('public passage retrieval', () => {
  it('waits for submission and stores the question in the URL', async () => {
    const search = vi.spyOn(publicCommunityApi, 'retrieve').mockResolvedValue(result)
    const test = open()
    expect(search).not.toHaveBeenCalled()
    await userEvent.type(screen.getByLabelText('Bạn đang cần hỗ trợ gì?'), 'doi mk')
    expect(search).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Tìm đoạn hướng dẫn' }))
    expect(await screen.findByText(passage.text)).toBeInTheDocument()
    expect(test.router.state.location.search).toBe('?question=doi+mk')
    expect(search).toHaveBeenCalledWith('doi mk', expect.any(AbortSignal))
    expect(screen.getByRole('link', { name: passage.title })).toHaveAttribute('href', `/knowledge/${id}`)
    expect(screen.getByText(/Bước 2/)).toBeInTheDocument()
    expect(screen.getByText(/Phiên bản 3/)).toBeInTheDocument()
    expect(screen.getByText(/30\/10\/2026/)).toBeInTheDocument()
    expect(screen.getByText(passage.applicability)).toBeInTheDocument()
  })
  it('restores a direct question link without a session and shows empty results honestly', async () => {
    vi.spyOn(publicCommunityApi, 'retrieve').mockResolvedValue({ ...result, items: [] })
    open('/knowledge?question=khongtimthay')
    expect(await screen.findByText('Chưa tìm thấy đoạn hướng dẫn phù hợp')).toBeInTheDocument()
    expect(screen.getByText(/dữ liệu minh họa và bản nháp không được dùng/)).toBeInTheDocument()
  })
  it('renders excerpts as text and reports bounded candidates without inventing confidence', async () => {
    const malicious = '<img src=x onerror=alert(1)>'
    vi.spyOn(publicCommunityApi, 'retrieve').mockResolvedValue({ ...result, candidateLimitReached: true, items: [{ ...passage, text: malicious, sourceUpdatedOn: null }] })
    open('/knowledge?question=VPN')
    expect(await screen.findByText(malicious)).toBeInTheDocument()
    expect(document.querySelector('img')).toBeNull()
    expect(screen.getByText(/Có nhiều tài liệu phù hợp/)).toBeInTheDocument()
    expect(screen.getByText(/Chưa xác định/)).toBeInTheDocument()
  })
  it('removes a withdrawn excerpt on the same public invalidation as the catalog', async () => {
    const search = vi.spyOn(publicCommunityApi, 'retrieve').mockResolvedValue(result)
    const test = open('/knowledge?question=doi+mk')
    await screen.findByText(passage.text)
    search.mockResolvedValue({ ...result, items: [] })
    await act(async () => { await test.client.resetQueries({ queryKey: publicCommunityKey }) })
    await screen.findByText('Chưa tìm thấy đoạn hướng dẫn phù hợp')
    expect(screen.queryByText(passage.text)).not.toBeInTheDocument()
  })
  it('shows safe API errors with retry instead of falling back to demo or Wikipedia', async () => {
    const search = vi.spyOn(publicCommunityApi, 'retrieve').mockRejectedValue(new ApiError('Vui lòng thử lại sau', 'http', 429))
    open('/knowledge?question=VPN')
    expect(await screen.findByRole('alert')).toHaveTextContent('Vui lòng thử lại sau')
    expect(search).toHaveBeenCalledTimes(1)
    search.mockResolvedValue(result)
    await userEvent.click(screen.getByRole('button', { name: /Thử lại/ }))
    await screen.findByText(passage.text)
  })
  it('blocks malformed direct links before requesting the API', async () => {
    const search = vi.spyOn(publicCommunityApi, 'retrieve').mockResolvedValue(result)
    open('/knowledge?question=x')
    expect(screen.getByRole('alert')).toHaveTextContent('2 đến 200 ký tự')
    await waitFor(() => expect(search).not.toHaveBeenCalled())
  })
})
