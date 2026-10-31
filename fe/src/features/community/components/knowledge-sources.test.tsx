import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { WikipediaReferences, BookStackImport } from './knowledge-sources'
import { publicCommunityApi } from '../api/community-api'
import { createAppServices } from '../../../app/providers/app-services'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { tokenPair, userA } from '../../../test/auth-fixtures'
import { ApiError } from '../../../shared/api/api-error'

const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(fn => fn()); vi.restoreAllMocks() })
function openWikipedia() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  disposals.push(() => client.clear())
  render(<QueryClientProvider client={client}><WikipediaReferences /></QueryClientProvider>)
}
async function openBookStack(fail = false) {
  const requests: InternalAxiosRequestConfig[] = []
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: 'OK', headers: new AxiosHeaders(), data: undefined as unknown }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('sources', userA) }
    if (config.url === '/api/v1/users/me') return { ...response, data: userA }
    if (fail) throw new AxiosError('Unavailable', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status: 503, data: { title: 'Nguồn chưa được bật', code: 'SourceDisabled' } })
    if (config.method === 'post') return { ...response, data: { entry: { id: 'imported' }, alreadyImported: false } }
    if (config.url?.endsWith('/12')) return { ...response, data: { pageId: 12, title: 'VPN wiki', content: '<script>private()</script> Hướng dẫn', revision: '2', sourceUrl: null, attribution: 'Nhóm IT', license: 'Được đơn vị cho phép' } }
    return { ...response, data: { items: [{ id: 12, title: 'VPN wiki', updatedAt: null }], page: 1, pageSize: 12, totalCount: 1 } }
  }
  const services = createAppServices({ baseURL: '/', adapter })
  await services.session.login({ email: userA.email, password: 'unit-test-only-password' })
  const router = createMemoryRouter([{ path: '/admin/knowledge', element: <BookStackImport /> }, { path: '/admin/knowledge/:id', element: <h1>Biên tập bản nháp</h1> }], { initialEntries: ['/admin/knowledge'] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { requests, router }
}

describe('external knowledge sources', () => {
  it('sends no Wikipedia search until the reader explicitly submits keywords', async () => {
    const search = vi.spyOn(publicCommunityApi, 'wikipedia').mockResolvedValue({ items: [], nextOffset: null })
    openWikipedia()
    expect(search).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Mở tra cứu Wikipedia' }))
    await userEvent.type(screen.getByLabelText('Từ khóa Wikipedia'), 'DNS')
    expect(search).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Tìm trên Wikipedia' }))
    await screen.findByText('Chưa tìm thấy tài liệu tham khảo')
    expect(search).toHaveBeenCalledExactlyOnceWith('DNS', 0, expect.any(AbortSignal))
  })
  it('renders external titles as text and uses protected links to the original page', async () => {
    vi.spyOn(publicCommunityApi, 'wikipedia').mockResolvedValue({ items: [{ title: '<img src=x> DNS', url: 'https://vi.wikipedia.org/?curid=42', updatedAt: null }], nextOffset: null })
    openWikipedia()
    await userEvent.click(screen.getByRole('button', { name: 'Mở tra cứu Wikipedia' }))
    await userEvent.type(screen.getByLabelText('Từ khóa Wikipedia'), 'DNS')
    await userEvent.click(screen.getByRole('button', { name: 'Tìm trên Wikipedia' }))
    const link = await screen.findByRole('link', { name: /<img src=x> DNS/ })
    expect(link).toHaveAttribute('href', 'https://vi.wikipedia.org/?curid=42')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    expect(document.querySelector('img')).toBeNull()
  })
  it('uses server continuation for Wikipedia pagination', async () => {
    const search = vi.spyOn(publicCommunityApi, 'wikipedia').mockResolvedValue({ items: [], nextOffset: 5 })
    openWikipedia()
    await userEvent.click(screen.getByRole('button', { name: 'Mở tra cứu Wikipedia' }))
    await userEvent.type(screen.getByLabelText('Từ khóa Wikipedia'), 'VPN')
    await userEvent.click(screen.getByRole('button', { name: 'Tìm trên Wikipedia' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Wikipedia: trang sau' }))
    await waitFor(() => expect(search).toHaveBeenLastCalledWith('VPN', 5, expect.any(AbortSignal)))
  })
  it('keeps Wikipedia failures within the reference panel', async () => {
    vi.spyOn(publicCommunityApi, 'wikipedia').mockRejectedValue(new ApiError('Wikipedia tạm thời lỗi', 'http', 503))
    openWikipedia()
    await userEvent.click(screen.getByRole('button', { name: 'Mở tra cứu Wikipedia' }))
    await userEvent.type(screen.getByLabelText('Từ khóa Wikipedia'), 'DNS')
    await userEvent.click(screen.getByRole('button', { name: 'Tìm trên Wikipedia' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Wikipedia tạm thời lỗi')
    expect(screen.getByLabelText('Từ khóa Wikipedia')).toHaveValue('DNS')
  })
  it('previews private wiki content before an explicit draft import and never auto-publishes', async () => {
    const test = await openBookStack()
    expect(test.requests.some(r => r.url?.includes('bookstack'))).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Nhập từ BookStack' }))
    await userEvent.click(await screen.findByRole('button', { name: 'VPN wiki · Xem trước' }))
    await screen.findByText('<script>private()</script> Hướng dẫn')
    expect(document.querySelector('script')).toBeNull()
    const button = screen.getByRole('button', { name: 'Nhập / mở bản nháp đã nhập' })
    expect(button).toBeDisabled()
    await userEvent.click(screen.getByLabelText(/Tôi hiểu bài nhập/))
    await userEvent.click(button)
    await screen.findByRole('heading', { name: 'Biên tập bản nháp' })
    const post = test.requests.filter(r => r.url?.endsWith('/imports'))
    expect(post).toHaveLength(1)
    const imported = post[0]
    if (!imported) throw new Error('Expected an explicit import request')
    expect(JSON.parse(imported.data)).toEqual({ pageId: 12 })
    expect(test.requests.some(r => r.url?.endsWith('/publish'))).toBe(false)
  })
  it('explains configuration when the private wiki connector is disabled', async () => {
    await openBookStack(true)
    await userEvent.click(screen.getByRole('button', { name: 'Nhập từ BookStack' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Nguồn chưa được bật')
    expect(screen.getByText(/quản trị viên cần cấu hình URL/)).toBeInTheDocument()
  })
})
