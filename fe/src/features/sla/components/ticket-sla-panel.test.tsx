import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { AuthProvider } from '../../auth/providers/auth-provider'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import type { TicketSla } from '../types/sla'
import { TicketSlaPanel } from './ticket-sla-panel'

const id = 'b0000000-0000-0000-0000-000000000001'
const snapshot: TicketSla = { ticketId: id, status: 3, asOf: '2026-10-29T00:00:00Z', sla: {
  policyId: 'b0000000-0000-0000-0000-000000000002', policyName: 'Cam kết lịch sử', priority: 2,
  firstResponseMinutes: 60, resolutionMinutes: 240, appliedAt: '2026-10-01T00:00:00Z',
  firstResponseDueAt: '2026-10-01T01:00:00Z', resolutionDueAt: '2026-10-01T04:00:00Z',
  firstRespondedAt: null, resolvedAt: null, firstResponseBreached: false, resolutionBreached: false,
  firstResponseBreachedAt: null, resolutionBreachedAt: null,
} }
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()) })
async function fixture(manage = false) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: { value: TicketSla; status: number; gate?: Promise<TicketSla> } = { value: structuredClone(snapshot), status: 200 }
  const profile = { ...userA, permissions: manage ? ['ticket.read.own', 'ticket.manage.sla'] : ['ticket.read.own'] }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('sla', profile) }
    if (config.url === '/api/v1/users/me') return { ...response, data: profile }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url === `/api/v1/tickets/${id}/sla`) {
      if (backend.status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status: backend.status, data: { title: 'SLA unavailable', traceId: 'sla-read' } })
      return { ...response, data: backend.gate ? await backend.gate : backend.value }
    }
    throw new Error('Unexpected SLA panel request')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: userA.email, password: 'test-only' })
  disposals.push(() => services.queryClient.clear())
  const onDenied = vi.fn()
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}>
    <MemoryRouter><TicketSlaPanel id={id} onDenied={onDenied} /></MemoryRouter></AuthProvider></QueryClientProvider>)
  return { backend, services, requests, onDenied, mount }
}
describe('Ticket SLA server authority and private lifecycle', () => {
  it('reads historical snapshot with only Ticket read permission and never loads the current policy', async () => {
    const test = await fixture(); test.mount()
    await screen.findByText('Cam kết lịch sử')
    expect(screen.getByText('60 phút')).toBeInTheDocument()
    expect(screen.getByText(/Chờ người yêu cầu không tạm dừng/)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Xem policy hiện tại' })).not.toBeInTheDocument()
    expect(test.requests.filter(r => r.url?.includes('/sla'))).toHaveLength(1)
    expect(test.requests.at(-1)?.headers.get('Authorization')).toBe('Bearer access-sla')
  })
  it('links policy administration only for its separate grant without fetching it', async () => {
    const test = await fixture(true); test.mount(); await screen.findByText('Cam kết lịch sử')
    expect(screen.getByRole('link', { name: 'Xem policy hiện tại' })).toHaveAttribute('href', `/admin/sla-policies/${snapshot.sla!.policyId}`)
    expect(test.requests.some(r => r.url?.includes('/admin/sla-policies'))).toBe(false)
  })
  it('represents a successful null snapshot separately from a read error', async () => {
    const test = await fixture(); test.backend.value.sla = null; test.mount()
    await screen.findByRole('heading', { name: 'Yêu cầu chưa có SLA' })
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Giải quyết' })).not.toBeInTheDocument()
  })
  it('does not fill missing legacy targets from the current policy', async () => {
    const test = await fixture(); Object.assign(test.backend.value.sla!, { policyName: null, priority: null, firstResponseMinutes: null, resolutionMinutes: null }); test.mount()
    await screen.findByText('Không lưu tên policy lịch sử')
    expect(screen.getAllByText('Không lưu mục tiêu lịch sử')).toHaveLength(2)
    expect(screen.getByText('Không lưu snapshot ưu tiên')).toBeInTheDocument()
  })
  it('uses server breach flags even for deadlines in the past and permits a breach without a worker marker', async () => {
    const test = await fixture(); test.backend.value.sla!.resolutionBreached = true; test.mount(); await screen.findByText('Cam kết lịch sử')
    expect(within(screen.getByRole('region', { name: 'Phản hồi công khai đầu tiên' })).getByText('Chưa ghi nhận vi phạm SLA')).toBeInTheDocument()
    const resolution = within(screen.getByRole('region', { name: 'Giải quyết' }))
    expect(resolution.getByText('Đã vi phạm SLA')).toBeInTheDocument()
    expect(resolution.getByText(/chưa có mốc ghi nhận từ command\/worker/)).toBeInTheDocument()
  })
  it('shows first response, resolution and historical breach markers from the server', async () => {
    const test = await fixture(); test.backend.value.status = 4
    Object.assign(test.backend.value.sla!, { firstRespondedAt: '2026-10-01T02:00:00Z', resolvedAt: '2026-10-01T05:00:00Z', firstResponseBreached: true,
      resolutionBreached: true, firstResponseBreachedAt: '2026-10-01T02:00:00Z', resolutionBreachedAt: '2026-10-01T05:00:00Z' })
    test.mount(); await screen.findByText('Đã ghi nhận phản hồi hợp lệ')
    expect(screen.getByText('Đã giải quyết')).toBeInTheDocument()
    expect(screen.getAllByText('Đã vi phạm SLA')).toHaveLength(2)
    expect(screen.getByText(/resolution summary không thay thế phản hồi/)).toBeInTheDocument()
    expect(screen.getByText(/Múi giờ hiển thị:/)).toBeInTheDocument()
  })
  it.each([403, 404])('asks the resource boundary to revalidate after HTTP %i', async status => {
    const test = await fixture(); test.backend.status = status; test.mount()
    await waitFor(() => expect(test.onDenied).toHaveBeenCalledOnce())
    expect(screen.queryByText('Cam kết lịch sử')).not.toBeInTheDocument()
  })
  it('keeps a transient error separate and supports manual retry', async () => {
    const test = await fixture(); test.backend.status = 503; test.mount(); await screen.findByRole('alert')
    expect(test.onDenied).not.toHaveBeenCalled()
    test.backend.status = 200; await userEvent.click(screen.getByRole('button', { name: 'Tải lại SLA' }))
    await screen.findByText('Cam kết lịch sử')
  })
  it('hides stale content while revalidating and clears it when scope is lost', async () => {
    const test = await fixture(); const mounted = test.mount(); await screen.findByText('Cam kết lịch sử')
    const gate = deferred<TicketSla>(); test.backend.gate = gate.promise
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại SLA' }))
    expect(screen.queryByText('Cam kết lịch sử')).not.toBeInTheDocument()
    mounted.unmount()
    await act(async () => { await test.services.session.logout(); gate.resolve(snapshot); await gate.promise })
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
  })
})
