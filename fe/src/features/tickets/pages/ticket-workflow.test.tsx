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
import { ticketKeys } from '../hooks/use-tickets'
import type { TicketDetails } from '../types/ticket'
import type { AssignmentTeam, TicketMutationResult, TicketWorkflowContext } from '../types/ticket-workflow'
import { noAttachmentUpload } from '../../../test/attachment-fixtures'
import { noSlaForPath } from '../../../test/sla-fixtures'

const id = '90000000-0000-0000-0000-000000000001'
const teamA = { id: '90000000-0000-0000-0000-000000000002', name: 'Nhóm mạng', departmentName: 'Hạ tầng' }
const teamB = { ...teamA, id: '90000000-0000-0000-0000-000000000003', name: 'Nhóm ứng dụng' }
const agent = { id: '90000000-0000-0000-0000-000000000004', displayName: 'Trần Bình' }
const page = <T,>(items: T[], number = 1, totalCount = items.length) => ({ items, page: number, pageSize: 20, totalCount })
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); disposals.splice(0).forEach(dispose => dispose()) })

async function fixture() {
  const requests: InternalAxiosRequestConfig[] = []
  const profile = { ...userA, roles: ['CustomRole'], permissions: ['ticket.read.all', 'ticket.assign', 'ticket.update.status', 'ticket.confirm', 'ticket.reopen', 'ticket.cancel'] }
  const backend: {
    context: TicketWorkflowContext; detail: TicketDetails; accessStatus: number; postStatus: number; postCode: string
    failure?: ApiError; postGate?: Promise<TicketMutationResult>; accessGate?: Promise<void>; contextGate?: Promise<void>
    teamsGate?: Promise<void>; afterCommit?: () => void; teams: AssignmentTeam[]
  } = {
    context: { version: 81, status: 2, canAssign: true, assignmentIsValid: true, transitions: [3, 4],
      assignedTeamId: teamA.id, assignedTeamName: teamA.name, assignedAgentId: agent.id, assignedAgentName: agent.displayName },
    detail: { id, ticketNumber: 42, title: 'Yêu cầu workflow', description: 'private-workflow-description', status: 2, priority: 2,
      requesterId: agent.id, categoryId: teamB.id, assignedTeamId: teamA.id, assignedAgentId: agent.id, createdAt: '2026-10-29T00:00:00Z', updatedAt: null,
      resolutionSummary: null, resolvedAt: null, closedAt: null, version: 81 },
    accessStatus: 200, postStatus: 200, postCode: 'VersionConflict', teams: [teamA, teamB],
  }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number, code?: string) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined, { ...response, status,
        headers: new AxiosHeaders({ 'Retry-After': '30' }), data: { title: 'Thao tác bị từ chối.', code, traceId: 'workflow-trace', errors: { ResolutionSummary: ['Kết quả chưa hợp lệ.'] } } })
    }
    if (config.url === '/api/v1/auth/login' || config.url === '/api/v1/auth/refresh') return { ...response, data: tokenPair('workflow', profile) }
    if (config.url === '/api/v1/notifications/unread-count') return { ...response, data: { count: 0 } }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url?.endsWith('/sla')) return { ...response, data: noSlaForPath(config.url) }
    if (config.url?.endsWith('/attachments/options')) return { ...response, data: noAttachmentUpload }
    if (config.url?.endsWith('/attachments')) return { ...response, data: page([]) }
    if (config.url === '/api/v1/users/me') return { ...response, data: profile }
    if (config.url === `/api/v1/tickets/${id}/access`) {
      await backend.accessGate; fail(backend.accessStatus)
      return { ...response, data: { canRead: true, canAssign: backend.context.canAssign, canUpdateStatus: true } }
    }
    if (config.url?.endsWith('/workflow')) { await backend.contextGate; fail(backend.accessStatus); return { ...response, data: { ...backend.context } } }
    if (config.url === `/api/v1/tickets/${id}`) { fail(backend.accessStatus); return { ...response, data: { ...backend.detail } } }
    if (config.url?.endsWith('/assignment-teams')) {
      await backend.teamsGate
      const teams = backend.teams.filter(team => team.name.includes(config.params.search) || team.departmentName.includes(config.params.search))
      return { ...response, data: page(config.params.page === 1 ? teams : [], config.params.page, teams.length + (config.params.search ? 0 : 20)) }
    }
    if (config.url?.endsWith('/agents')) return { ...response, data: page([agent]) }
    if (/\/(assign|transition)$/.test(config.url ?? '')) {
      if (backend.failure) throw backend.failure
      fail(backend.postStatus, backend.postCode)
      if (backend.postGate) return { ...response, data: await backend.postGate }
      const value = JSON.parse(config.data)
      const version = backend.context.version + 1
      if (config.url?.endsWith('/assign')) {
        backend.context = { ...backend.context, assignedTeamId: value.teamId, assignedAgentId: value.agentId, version }
        backend.detail = { ...backend.detail, assignedTeamId: value.teamId, assignedAgentId: value.agentId, version }
      } else {
        backend.context = { ...backend.context, version, status: value.status, transitions: value.status === 4 ? [5, 7] : [] }
        backend.detail = { ...backend.detail, version, status: value.status, resolutionSummary: value.resolutionSummary }
      }
      backend.afterCommit?.()
      return { ...response, data: { id, version, status: backend.detail.status, assignedTeamId: backend.detail.assignedTeamId, assignedAgentId: backend.detail.assignedAgentId, updatedAt: null, resolvedAt: null, closedAt: null } }
    }
    if (config.url?.endsWith('/discussion-access')) return { ...response, data: { canReadInternal: false, canCommentPublic: false, canCommentInternal: false } }
    if (/\/(activities|comments)$/.test(config.url ?? '')) return { ...response, data: page([]) }
    if (config.url === '/api/v1/tickets') return { ...response, data: page([]) }
    throw new Error('Unexpected fixture route')
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: profile.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [`/tickets/${id}`] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}>
    <RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  const posts = () => requests.filter(config => /\/(assign|transition)$/.test(config.url ?? ''))
  return { backend, services, router, requests, mount, posts }
}
async function chooseResolve() {
  await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Thao tác' }), '4')
  await userEvent.type(screen.getByRole('textbox', { name: 'Kết quả giải quyết' }), 'Đã khôi phục kết nối.')
}
async function confirmTransition() {
  await userEvent.click(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' }))
  await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận chuyển trạng thái' })).getByRole('button', { name: 'Xác nhận thao tác' }))
}
async function pickTeam(name = 'Nhóm ứng dụng · Hạ tầng') {
  await userEvent.click(await screen.findByRole('button', { name: 'Chọn nhóm hỗ trợ' }))
  await userEvent.click(await within(screen.getByRole('dialog', { name: 'Chọn nhóm hỗ trợ' })).findByRole('button', { name }))
}
async function pickAgent(groupOnly = false) {
  await userEvent.click(screen.getByRole('button', { name: 'Chọn người xử lý' }))
  await userEvent.click(await within(screen.getByRole('dialog', { name: 'Chọn người xử lý' })).findByRole('button', { name: groupOnly ? 'Chỉ giao nhóm, bỏ người xử lý' : `${agent.displayName} · ${agent.id.slice(-8)}` }))
}

describe('Ticket workflow from server capabilities', () => {
  it('waits for resource access and current context; never derives actions from role or broad access', async () => {
    const test = await fixture(); const gate = deferred<void>(); test.backend.accessGate = gate.promise; test.mount()
    expect(test.requests.some(request => /\/(workflow|assignment-teams)$/.test(request.url ?? ''))).toBe(false)
    test.backend.context = { ...test.backend.context, canAssign: false, transitions: [] }
    await act(async () => { gate.resolve(); await gate.promise })
    await screen.findByText('Hiện không có bước chuyển trạng thái được phép.')
    expect(screen.queryByRole('button', { name: 'Chọn nhóm hỗ trợ' })).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Thao tác' })).not.toBeInTheDocument()
    expect(test.requests.some(request => /support-teams|\/users\?|\/users$/.test(request.url ?? ''))).toBe(false)
  })
  it('requires a public resolution summary and sends only workflow DTO plus server version', async () => {
    const test = await fixture(); test.mount()
    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Thao tác' }), '4')
    await userEvent.click(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' }))
    expect(await screen.findByText(/Vui lòng nhập kết quả giải quyết, tối đa/)).toBeInTheDocument()
    expect(test.posts()).toHaveLength(0)
    await userEvent.type(screen.getByRole('textbox', { name: 'Kết quả giải quyết' }), 'Đã sửa.')
    await confirmTransition()
    await screen.findByText('Đã chuyển trạng thái yêu cầu.')
    expect(JSON.parse(test.posts()[0]!.data)).toEqual({ status: 4, resolutionSummary: 'Đã sửa.', version: 81 })
    await screen.findByText('Kết quả giải quyết')
    expect(test.posts()[0]!.headers.get('Authorization')).toBe('Bearer access-workflow')
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
    expect(screen.queryByRole('textbox', { name: /Tiêu đề|Độ ưu tiên/ })).not.toBeInTheDocument()
  })
  it.each([5, 6, 7] as const)('supports requester/global action %i only when returned by server and sends no summary', async status => {
    const test = await fixture(); test.backend.context = { ...test.backend.context, canAssign: false, status: status === 6 ? 1 : 4, transitions: [status] }
    test.mount()
    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Thao tác' }), String(status))
    await confirmTransition()
    await screen.findByText('Đã chuyển trạng thái yêu cầu.')
    expect(JSON.parse(test.posts()[0]!.data)).toEqual({ status, resolutionSummary: null, version: 81 })
  })
  it('keeps a resolution draft when switching to wait, but never sends it for a non-resolve transition', async () => {
    const test = await fixture(); test.mount(); await chooseResolve()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Thao tác' }), '3')
    expect(screen.getByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
    await confirmTransition(); await screen.findByText('Đã chuyển trạng thái yêu cầu.')
    expect(JSON.parse(test.posts()[0]!.data).resolutionSummary).toBeNull()
  })
  it('uses paged searchable Ticket-specific choices and explicitly chooses agent or group-only after changing team', async () => {
    const test = await fixture(); test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Chọn nhóm hỗ trợ' }))
    const dialog = screen.getByRole('dialog', { name: 'Chọn nhóm hỗ trợ' })
    await within(dialog).findByRole('button', { name: 'Nhóm mạng · Hạ tầng' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Trang sau' }))
    await within(dialog).findByText('Không có lựa chọn phù hợp')
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Tìm nhóm hoặc phòng ban' }), 'ứng dụng{Enter}')
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Nhóm ứng dụng · Hạ tầng' }))
    expect(screen.getByRole('button', { name: 'Lưu phân công' })).toBeDisabled()
    await pickAgent()
    await pickTeam('Nhóm mạng · Hạ tầng')
    expect(screen.getByRole('button', { name: 'Lưu phân công' })).toBeDisabled()
    await pickAgent(true)
    await userEvent.click(screen.getByRole('button', { name: 'Lưu phân công' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận phân công' })).getByRole('button', { name: 'Xác nhận thao tác' }))
    await screen.findByText('Đã phân công yêu cầu.')
    expect(JSON.parse(test.posts()[0]!.data)).toEqual({ teamId: teamA.id, agentId: null, version: 81 })
    const searches = test.requests.filter(request => request.url?.endsWith('/assignment-teams'))
    expect(searches.some(request => request.params.page === 2)).toBe(true)
    expect(searches.some(request => request.params.search === 'ứng dụng' && request.params.page === 1)).toBe(true)
    expect(test.requests.some(request => request.url?.startsWith('/api/v1/support-teams') || request.url?.startsWith('/api/v1/users/lookup'))).toBe(false)
  })
  it.each(['VersionConflict', 'InvalidTransition'])('preserves draft on %s, reads fresh context, and requires review plus a separate resubmit', async code => {
    const test = await fixture(); test.backend.postStatus = 409; test.backend.postCode = code; test.mount(); await chooseResolve(); await confirmTransition()
    await screen.findByText(/Mã tra cứu: workflow-trace/)
    expect(screen.getByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
    expect(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' })).toBeDisabled()
    test.backend.context = { ...test.backend.context, version: 99 }; test.backend.detail = { ...test.backend.detail, version: 99 }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại để xem xét' }))
    await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' })
    expect(test.posts()).toHaveLength(1)
    expect(screen.getByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
    await userEvent.click(screen.getByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' }))
    test.backend.postStatus = 200
    await confirmTransition(); await screen.findByText('Đã chuyển trạng thái yêu cầu.')
    expect(test.posts()).toHaveLength(2)
    expect(JSON.parse(test.posts()[1]!.data)).toEqual({ status: 4, resolutionSummary: 'Đã khôi phục kết nối.', version: 99 })
  })
  it('keeps summary and blocks an action removed by conflict recovery instead of replaying it', async () => {
    const test = await fixture(); test.backend.postStatus = 409; test.mount(); await chooseResolve(); await confirmTransition()
    await screen.findByText(/Mã tra cứu: workflow-trace/)
    test.backend.context = { ...test.backend.context, version: 99, status: 3, transitions: [2] }; test.backend.detail = { ...test.backend.detail, version: 99, status: 3 }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại để xem xét' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' }))
    expect(screen.getByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
    expect(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' })).toBeDisabled()
    expect(screen.getByText(/Thao tác đã chọn không còn được phép/)).toBeInTheDocument()
    expect(test.posts()).toHaveLength(1)
  })
  it('does not overwrite drafts or adopt a newer server version on background revalidation', async () => {
    const test = await fixture(); test.mount(); await chooseResolve()
    test.backend.context = { ...test.backend.context, version: 99 }; test.backend.detail = { ...test.backend.detail, version: 99 }
    await act(async () => { await test.services.queryClient.invalidateQueries({ queryKey: ticketKeys.resource(test.services.session.store.getState().scope, id) }) })
    expect(await screen.findByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
    expect(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' })).toBeDisabled()
    expect(test.posts()).toHaveLength(0)
  })
  it('keeps assignment choices on conflict and requires fresh selection before writing with the reviewed version', async () => {
    const test = await fixture(); test.backend.postStatus = 409; test.mount(); await pickTeam(); await pickAgent()
    await userEvent.click(screen.getByRole('button', { name: 'Lưu phân công' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận phân công' })).getByRole('button', { name: 'Xác nhận thao tác' }))
    await screen.findByText(/Mã tra cứu: workflow-trace/)
    expect(screen.getByText('Nhóm ứng dụng · Hạ tầng')).toBeInTheDocument()
    test.backend.context = { ...test.backend.context, version: 99 }; test.backend.detail = { ...test.backend.detail, version: 99 }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại để xem xét' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' }))
    expect(screen.getByText('Nhóm ứng dụng · Hạ tầng')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Lưu phân công' })).toBeDisabled()
    await pickTeam(); await pickAgent(); test.backend.postStatus = 200
    await userEvent.click(screen.getByRole('button', { name: 'Lưu phân công' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận phân công' })).getByRole('button', { name: 'Xác nhận thao tác' }))
    await screen.findByText('Đã phân công yêu cầu.')
    expect(test.posts()).toHaveLength(2)
    expect(JSON.parse(test.posts()[1]!.data)).toEqual({ teamId: teamB.id, agentId: agent.id, version: 99 })
  })
  it('keeps a successful command successful when verification fails and permits only explicit read recovery', async () => {
    const test = await fixture(); test.mount(); await chooseResolve()
    test.backend.afterCommit = () => { test.backend.accessStatus = 503 }
    await confirmTransition(); await screen.findByRole('button', { name: 'Thử lại' })
    expect(screen.getByText('Đã chuyển trạng thái yêu cầu.')).toBeInTheDocument()
    expect(test.posts()).toHaveLength(1)
    expect(screen.queryByText('private-workflow-description')).not.toBeInTheDocument()
    test.backend.accessStatus = 200
    await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }))
    await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' })
    expect(test.posts()).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' })).toBeDisabled()
  })
  it('does not render actions when context and displayed detail have different server versions', async () => {
    const test = await fixture(); test.backend.context = { ...test.backend.context, version: 99 }; test.mount()
    await screen.findByRole('combobox', { name: 'Thao tác' })
    expect(screen.getByRole('button', { name: 'Chọn nhóm hỗ trợ' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' })).toBeDisabled()
    expect(test.posts()).toHaveLength(0)
  })
  it('records committed assignment then purges private content if follow-up read returns 404', async () => {
    const test = await fixture(); test.mount(); await pickTeam(); await pickAgent()
    test.backend.afterCommit = () => { test.backend.accessStatus = 404 }
    await userEvent.click(screen.getByRole('button', { name: 'Lưu phân công' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận phân công' })).getByRole('button', { name: 'Xác nhận thao tác' }))
    await screen.findByRole('heading', { name: 'Không tìm thấy yêu cầu' })
    expect(screen.getByText('Đã phân công yêu cầu.')).toBeInTheDocument()
    expect(screen.queryByText('private-workflow-description')).not.toBeInTheDocument()
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain('private-workflow-description')
    expect(test.posts()).toHaveLength(1)
  })
  it.each([403, 404])('hides resource and purges cache after HTTP %i without token refresh', async status => {
    const test = await fixture(); test.backend.postStatus = status; test.mount(); await chooseResolve(); await confirmTransition()
    await screen.findByRole('heading', { name: status === 403 ? 'Không có quyền truy cập' : 'Không tìm thấy yêu cầu' })
    await waitFor(() => expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(query => query.state.data))).not.toContain('private-workflow-description'))
    expect(test.requests.some(request => request.url === '/api/v1/auth/refresh')).toBe(false)
  })
  it('keeps field errors/traceId on 400 and retains the resolution text', async () => {
    const test = await fixture(); test.backend.postStatus = 400; test.backend.postCode = 'ResolutionRequired'; test.mount(); await chooseResolve(); await confirmTransition()
    await screen.findByText('Kết quả chưa hợp lệ.')
    expect(screen.getByText('Mã tra cứu: workflow-trace')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
  })
  it('honors 429 cooldown and never automatically replays a write', async () => {
    const test = await fixture(); test.backend.postStatus = 429; test.mount(); await chooseResolve(); await confirmTransition()
    await screen.findByText('Vui lòng chờ 30 giây trước khi thử lại.')
    expect(screen.getByRole('button', { name: 'Xem lại chuyển trạng thái' })).toBeDisabled()
    expect(test.posts()).toHaveLength(1)
  })
  it.each(['timeout', 'network'] as const)('blocks resubmission after an ambiguous %s, including after read/review', async kind => {
    const test = await fixture(); test.backend.failure = new ApiError('Mất kết nối.', kind); test.mount(); await chooseResolve(); await confirmTransition()
    await screen.findByText('Chưa xác định được kết quả thay đổi')
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại để xem xét' }))
    expect(await screen.findByRole('button', { name: 'Dùng phiên bản mới để tiếp tục' })).toBeDisabled()
    expect(screen.getByRole('textbox', { name: 'Kết quả giải quyết' })).toHaveValue('Đã khôi phục kết nối.')
    expect(test.posts()).toHaveLength(1)
  })
  it('ignores a late mutation receipt after logout; no private cache, refresh reads or success navigation', async () => {
    const test = await fixture(); const gate = deferred<TicketMutationResult>(); test.backend.postGate = gate.promise; test.mount(); await chooseResolve(); await confirmTransition()
    await waitFor(() => expect(test.posts()).toHaveLength(1))
    const count = test.requests.length
    await act(async () => { await test.services.session.logout() })
    await act(async () => { gate.resolve({ id, version: 82, status: 4, assignedTeamId: teamA.id, assignedAgentId: agent.id, updatedAt: null, resolvedAt: null, closedAt: null }); await gate.promise })
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
    expect(test.requests.slice(count).some(request => request.url?.startsWith('/api/v1/tickets'))).toBe(false)
    expect(screen.queryByText('Đã chuyển trạng thái yêu cầu.')).not.toBeInTheDocument()
    expect(screen.queryByText('private-workflow-description')).not.toBeInTheDocument()
  })
})
