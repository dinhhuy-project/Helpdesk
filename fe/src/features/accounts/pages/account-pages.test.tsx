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
import { Permission } from '../../auth/lib/permissions'
import type { UserProfile } from '../../auth/types/auth'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import type { AccountAccess, AccountUser, RoleAccess, RoleDetails } from '../types/account'

const targetId = '71000000-0000-0000-0000-000000000001'
const requesterId = '71000000-0000-0000-0000-000000000002'
const agentId = '71000000-0000-0000-0000-000000000003'
const adminId = '71000000-0000-0000-0000-000000000004'
const departmentId = '71000000-0000-0000-0000-000000000005'
const permissionId = '71000000-0000-0000-0000-000000000006'
const target: AccountUser = { id: targetId, email: 'account@example.test', displayName: 'Tài khoản thử nghiệm', isActive: true,
  departmentId: null, departmentName: null, createdAt: '2026-10-29T00:00:00Z', updatedAt: null, version: 91, roles: [{ id: requesterId, name: 'Requester' }] }
const roleData: RoleDetails[] = [
  { id: requesterId, name: 'Requester', description: null, version: 12, permissions: [Permission.TicketReadOwn] },
  { id: agentId, name: 'SupportAgent', description: null, version: 13, permissions: [] },
  { id: adminId, name: 'SystemAdmin', description: null, version: 14, permissions: [] },
]
const admin: UserProfile = { ...userA, roles: ['SystemAdmin'], permissions: [Permission.UserManage, Permission.RoleManage] }
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); for (const dispose of disposals.splice(0)) dispose() })
async function open(path = '/admin/users', profile = admin) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: {
    user: UserProfile; account: AccountUser; roles: RoleDetails[]; access: AccountAccess; roleAccess: RoleAccess
    status: number; code?: string; listStatus: number; detailStatus: number; count: number
    writeGate?: Promise<AccountUser>; listGate?: Promise<void>; nextProfile?: UserProfile
  } = { user: profile, account: structuredClone(target), roles: structuredClone(roleData),
    access: { canUpdateProfile: true, canChangeStatus: true, isLastActiveAdmin: false },
    roleAccess: { canManageGrants: true, canReadAudit: true, assignableRoleIds: [requesterId, agentId, adminId] },
    status: 200, listStatus: 200, detailStatus: 200, count: 1 }
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number, code?: string) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined,
        { ...response, status, data: { code, traceId: 'account-trace', title: 'Rejected' }, headers: new AxiosHeaders({ 'Retry-After': '30' }) })
    }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('accounts', backend.user) }
    if (config.url === '/api/v1/users/me') return { ...response, data: backend.user }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (config.url === '/api/v1/identity/administration-access') return { ...response, data: backend.roleAccess }
    if (config.url?.endsWith('/administration-access')) return { ...response, data: backend.access }
    if (config.method === 'get') {
      if (config.url === '/api/v1/identity/roles') return { ...response, data: backend.roles }
      if (config.url === '/api/v1/identity/permissions') return { ...response, data: [{ id: permissionId, code: Permission.TicketReadOwn }] }
      if (config.url === '/api/v1/identity/audit-logs') return { ...response, data: {
        items: [{ id: targetId, actorId: null, targetUserId: targetId, targetRoleId: null, action: 'UserCreated', data: '{"DefaultRoleId":"example"}', createdAt: target.createdAt }], page: Number(config.params.page), pageSize: 20, totalCount: 21 } }
      if (config.url === '/api/v1/departments/lookup') return { ...response, data: { items: [{ id: departmentId, name: 'Phòng ban hợp lệ' }], page: Number(config.params.page), pageSize: 20, totalCount: 21 } }
      if (config.url === '/api/v1/users' || config.url === '/api/v1/identity/assignment-users') {
        if (backend.listGate) await backend.listGate
        fail(backend.listStatus)
        return { ...response, data: { items: [backend.account], page: Number(config.params.page), pageSize: Number(config.params.pageSize), totalCount: backend.count } }
      }
      fail(backend.detailStatus)
      return { ...response, data: backend.account }
    }
    if (backend.writeGate) return { ...response, data: await backend.writeGate }
    fail(backend.status, backend.code)
    const body = JSON.parse(config.data) as Record<string, unknown>
    if (config.url?.endsWith('/permissions')) {
      const result = backend.roles.find(role => config.url?.includes(role.id))
      if (backend.nextProfile) backend.user = backend.nextProfile
      return { ...response, data: result }
    }
    backend.account = { ...backend.account, version: backend.account.version + 1 }
    if (typeof body.displayName === 'string') backend.account.displayName = body.displayName
    if ('departmentId' in body) backend.account.departmentId = typeof body.departmentId === 'string' ? body.departmentId : null
    if ('isActive' in body) backend.account.isActive = body.isActive === true
    return { ...response, status: config.method === 'post' ? 201 : 200, data: backend.account }
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: profile.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}>
    <AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider>
  </QueryClientProvider>)
  return { backend, requests, services, router, mount }
}
const writes = (requests: InternalAxiosRequestConfig[]) => requests.filter(request => request.url !== '/api/v1/auth/login' && request.method !== 'get')

describe('Account administration permission and query boundaries', () => {
  it.each(['/admin/users', '/admin/users/new', `/admin/users/${targetId}`, '/admin/roles', '/admin/roles/assignments', '/admin/roles/audit'])('does not bypass permission for role name at %s', async path => {
    const test = await open(path, { ...userA, roles: ['SystemAdmin'], permissions: [] })
    test.mount()
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(test.requests.filter(r => r.url?.startsWith('/api/v1/identity') || r.url === '/api/v1/users')).toHaveLength(0)
  })
  it('user manager sees users but no role assignment or role catalog', async () => {
    const test = await open(`/admin/users/${targetId}`, { ...userA, permissions: [Permission.UserManage] })
    test.mount()
    await screen.findByRole('heading', { name: target.displayName })
    expect(screen.queryByRole('link', { name: 'Quản lý vai trò của tài khoản này' })).not.toBeInTheDocument()
    expect(test.requests.some(r => r.url?.startsWith('/api/v1/identity'))).toBe(false)
    await act(async () => { await test.router.navigate('/admin/roles') })
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
  })
  it('role-only manager selects scoped targets and never requests the user-management APIs', async () => {
    const test = await open('/admin/roles/assignments', { ...userA, roles: ['Delegated'], permissions: [Permission.RoleManage] })
    test.backend.roleAccess = { canManageGrants: false, canReadAudit: false, assignableRoleIds: [requesterId] }
    test.mount()
    await userEvent.click(await screen.findByRole('link', { name: target.displayName }))
    await screen.findByRole('checkbox', { name: /Người yêu cầu/ })
    expect(screen.queryByRole('checkbox', { name: /Quản trị hệ thống/ })).not.toBeInTheDocument()
    expect(test.requests.some(r => r.method === 'get' && r.url?.startsWith('/api/v1/users/') && r.url !== '/api/v1/users/me')).toBe(false)
    expect(test.requests.some(r => r.url === '/api/v1/users')).toBe(false)
    await act(async () => { await test.router.navigate('/admin/roles/audit') })
    await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    expect(test.requests.some(r => r.url === '/api/v1/identity/audit-logs')).toBe(false)
  })
  it('keeps filters and server pagination in URL and restores drafts on browser back', async () => {
    const test = await open('/admin/users?page=2&search=test&isActive=false&sortBy=email&descending=false')
    test.backend.count = 21
    test.mount()
    await screen.findByRole('link', { name: target.displayName })
    expect(test.requests.find(r => r.url === '/api/v1/users')?.params).toMatchObject({ page: 2, search: 'test', isActive: false, sortBy: 'email', descending: false })
    expect(screen.getByText('Trang 2 / 2 · 21 kết quả')).toBeInTheDocument()
    await userEvent.clear(screen.getByRole('searchbox'))
    await userEvent.type(screen.getByRole('searchbox'), 'new search')
    await userEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' }))
    await waitFor(() => expect(test.requests.filter(r => r.url === '/api/v1/users').at(-1)?.params).toMatchObject({ page: 1, search: 'new search' }))
    await act(async () => { await test.router.navigate(-1) })
    expect(screen.getByRole('searchbox')).toHaveValue('test')
  })
  it.each(['/admin/users?page=0', '/admin/users?page=1&page=2', '/admin/users?isActive=1', '/admin/users?sortBy=passwordHash'])('rejects invalid server filters at %s', async path => {
    const test = await open(path)
    test.mount()
    expect(await screen.findByRole('heading', { name: 'Bộ lọc chưa hợp lệ' })).toBeInTheDocument()
    expect(test.requests.some(r => r.url === '/api/v1/users')).toBe(false)
  })
  it.each([403, 404])('hides a direct-link target on HTTP %i with no token refresh or write', async status => {
    const test = await open(`/admin/roles/assignments/${targetId}`)
    test.backend.detailStatus = status
    test.mount()
    await screen.findByRole('heading', { name: status === 403 ? 'Không có quyền truy cập' : 'Không tìm thấy tài khoản hoặc vai trò' })
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(test.requests.some(r => r.url === '/api/v1/auth/refresh')).toBe(false)
    expect(writes(test.requests)).toHaveLength(0)
  })
  it('never fetches an invalid resource identifier', async () => {
    const test = await open('/admin/users/not-a-guid')
    test.mount()
    expect(await screen.findByRole('heading', { name: 'Không tìm thấy tài khoản hoặc vai trò' })).toBeInTheDocument()
    expect(test.requests.some(r => r.url?.includes('not-a-guid'))).toBe(false)
  })
  it('removes private data when current permission is revoked, ignoring a late list response', async () => {
    const test = await open()
    const gate = deferred<void>()
    test.backend.listGate = gate.promise
    test.mount()
    test.backend.user = { ...admin, permissions: [] }
    await act(async () => { await test.services.session.refreshProfile() })
    await act(async () => { gate.resolve(); await gate.promise })
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(screen.queryByText(target.email)).not.toBeInTheDocument()
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
  })
})

describe('Account writes, concurrency, departments and revoked sessions', () => {
  async function fillCreate() {
    await userEvent.type(await screen.findByRole('textbox', { name: 'Email' }), 'new@example.test')
    await userEvent.type(screen.getByRole('textbox', { name: 'Tên hiển thị' }), 'Tài khoản mới')
    await userEvent.type(screen.getByLabelText(/^Mật khẩu ban đầu/), 'initial-password-test')
  }
  it('creates only the exact DTO and navigates to detail without caching the initial password', async () => {
    const test = await open('/admin/users/new')
    test.mount(); await fillCreate()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }))
    await screen.findByRole('heading', { name: 'Tài khoản mới' })
    const request = writes(test.requests)[0]
    expect(JSON.parse(request?.data)).toEqual({ email: 'new@example.test', displayName: 'Tài khoản mới', password: 'initial-password-test' })
    expect(test.router.state.location.pathname).toBe(`/admin/users/${targetId}`)
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
    expect(JSON.stringify(test.services.queryClient.getQueryCache().getAll().map(q => q.state.data))).not.toContain('initial-password-test')
  })
  it('blocks duplicate creation after uncertain timeout until the operator checks the list', async () => {
    const test = await open('/admin/users/new')
    const gate = deferred<AccountUser>()
    test.backend.writeGate = gate.promise
    test.mount(); await fillCreate()
    await userEvent.dblClick(screen.getByRole('button', { name: 'Tạo tài khoản' }))
    await act(async () => { gate.reject(new AxiosError('timeout', 'ECONNABORTED')); await gate.promise.catch(() => {}) })
    expect(await screen.findByRole('alert')).toHaveTextContent('Chưa xác định được kết quả')
    expect(screen.getByRole('button', { name: 'Tạo tài khoản' })).toBeDisabled()
    expect(writes(test.requests)).toHaveLength(1)
    expect(screen.getByRole('link', { name: 'Kiểm tra danh sách tài khoản' })).toBeInTheDocument()
  })
  it('does not restore a creation response after logout during the request', async () => {
    const test = await open('/admin/users/new')
    const gate = deferred<AccountUser>()
    test.backend.writeGate = gate.promise
    test.mount(); await fillCreate()
    await userEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }))
    await act(async () => { await test.services.session.logout() })
    await act(async () => { gate.resolve(target); await gate.promise })
    expect(test.services.session.store.getState().status).toBe('anonymous')
    expect(test.router.state.location.pathname).toBe('/login')
    expect(screen.queryByText(target.email)).not.toBeInTheDocument()
  })
  it('preserves drafts and rejects stale writes until explicit reload', async () => {
    const test = await open(`/admin/users/${targetId}`)
    test.backend.status = 409; test.backend.code = 'VersionConflict'
    test.mount()
    const field = await screen.findByRole('textbox', { name: 'Tên hiển thị' })
    await userEvent.clear(field); await userEvent.type(field, 'Bản nhập chưa lưu')
    await userEvent.click(screen.getByRole('button', { name: 'Lưu thông tin' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Dữ liệu đã được người khác thay đổi')
    expect(field).toHaveValue('Bản nhập chưa lưu')
    expect(screen.getByRole('button', { name: 'Lưu thông tin' })).toBeDisabled()
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ displayName: 'Bản nhập chưa lưu', departmentId: null, version: 91 })
    test.backend.status = 200; test.backend.account = { ...target, version: 94, displayName: 'Dữ liệu mới nhất' }
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại dữ liệu' }))
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Tên hiển thị' })).toHaveValue('Dữ liệu mới nhất'))
    expect(screen.getByRole('button', { name: 'Lưu thông tin' })).toBeEnabled()
  })
  it('retains historical department by default and selects a paged active lookup for a new assignment', async () => {
    const test = await open(`/admin/users/${targetId}`)
    test.backend.account.departmentId = adminId
    test.backend.account.departmentName = 'Phòng ban lịch sử'
    test.mount()
    await screen.findByRole('textbox', { name: 'Tên hiển thị' })
    expect(screen.getByText(/Giữ nguyên liên kết lịch sử/)).toBeInTheDocument()
    expect(screen.getByText('Phòng ban lịch sử')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Chọn phòng ban' }))
    const dialog = screen.getByRole('dialog', { name: 'Chọn phòng ban đang hoạt động' })
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Tìm phòng ban' }), 'Phòng ban{Enter}')
    await waitFor(() => expect(test.requests.filter(r => r.url === '/api/v1/departments/lookup').at(-1)?.params.search).toBe('Phòng ban'))
    expect(writes(test.requests)).toHaveLength(0)
    await userEvent.click(await screen.findByRole('button', { name: 'Trang sau' }))
    await waitFor(() => expect(test.requests.filter(r => r.url === '/api/v1/departments/lookup').at(-1)?.params.page).toBe(2))
    await userEvent.click(await screen.findByRole('button', { name: 'Phòng ban hợp lệ' }))
    await userEvent.click(screen.getByRole('button', { name: 'Lưu thông tin' }))
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toMatchObject({ departmentId, version: 91 })
    expect(test.requests.some(r => r.url === '/api/v1/departments')).toBe(false)
  })
  it('hides edit authority for protected users and prevents deactivating the last active admin', async () => {
    const test = await open(`/admin/users/${targetId}`)
    test.backend.access = { canUpdateProfile: false, canChangeStatus: false, isLastActiveAdmin: true }
    test.mount()
    await screen.findByRole('textbox', { name: 'Tên hiển thị' })
    expect(screen.getByRole('button', { name: 'Lưu thông tin' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Vô hiệu hóa tài khoản' })).toBeDisabled()
    expect(screen.getByText(/Đây là quản trị hệ thống hoạt động cuối cùng/)).toBeInTheDocument()
    expect(writes(test.requests)).toHaveLength(0)
  })
  it('discards local tokens and private cache immediately after confirmed self-deactivation', async () => {
    const test = await open(`/admin/users/${userA.id}`)
    test.backend.account = { ...target, id: userA.id }
    test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Vô hiệu hóa tài khoản' }))
    const dialog = screen.getByRole('dialog', { name: 'Xác nhận vô hiệu hóa tài khoản' })
    expect(dialog).toHaveTextContent('Bạn sẽ thoát khỏi phiên hiện tại')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận thay đổi trạng thái' }))
    await waitFor(() => expect(test.services.session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'revoked' }))
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
    expect(test.requests.some(r => r.url?.includes('reset-password') || r.url?.includes('revoke'))).toBe(false)
    expect(test.requests.some(r => r.url === '/api/v1/auth/logout')).toBe(false)
  })
  it('maps invalid department and rate-limit failures without refresh or retry', async () => {
    const test = await open(`/admin/users/${targetId}`)
    test.backend.status = 400; test.backend.code = 'InvalidDepartment'
    test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Lưu thông tin' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Chọn lại phòng ban hợp lệ')
    test.backend.status = 429; test.backend.code = undefined
    await userEvent.click(screen.getByRole('button', { name: 'Lưu thông tin' }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('30 giây'))
    expect(screen.getByRole('button', { name: 'Lưu thông tin' })).toBeDisabled()
    expect(writes(test.requests)).toHaveLength(2)
    expect(test.requests.some(r => r.url === '/api/v1/auth/refresh')).toBe(false)
  })
})

describe('Role assignment, grants and audit', () => {
  it('submits the complete selected role list with current user version', async () => {
    const test = await open(`/admin/roles/assignments/${targetId}`)
    test.mount()
    await userEvent.click(await screen.findByRole('checkbox', { name: /Nhân viên hỗ trợ/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Xem lại và lưu vai trò' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Xác nhận lưu vai trò' }))
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(writes(test.requests)[0]?.url).toBe(`/api/v1/users/${targetId}/roles`)
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ roleIds: [requesterId, agentId], version: 91 })
  })
  it('does not enable assignments if existing roles fall outside server scope', async () => {
    const test = await open(`/admin/roles/assignments/${targetId}`)
    test.backend.roleAccess.assignableRoleIds = [agentId]
    test.mount()
    expect(await screen.findByRole('alert')).toHaveTextContent('Vai trò hiện tại nằm ngoài phạm vi cấp')
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })
  it.each(['LastActiveAdmin', 'InsufficientScope'])('shows %s from role assignment without automatic replay', async code => {
    const test = await open(`/admin/roles/assignments/${targetId}`)
    test.backend.status = code === 'LastActiveAdmin' ? 409 : 403; test.backend.code = code
    test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Xem lại và lưu vai trò' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Xác nhận lưu vai trò' }))
    const error = await screen.findByRole('alert')
    expect(error).toHaveTextContent(code === 'LastActiveAdmin' ? 'quản trị hệ thống hoạt động cuối cùng' : 'ngoài phạm vi cấp quyền')
    expect(writes(test.requests)).toHaveLength(1)
  })
  it('shows grants read-only and avoids fetching permission catalog for a delegated manager', async () => {
    const test = await open(`/admin/roles/${requesterId}`)
    test.backend.roleAccess.canManageGrants = false
    test.mount()
    expect(await screen.findByText('Bạn có quyền xem vai trò, không có quyền thay đổi grant.')).toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(test.requests.some(r => r.url === '/api/v1/identity/permissions')).toBe(false)
  })
  it('preserves protected admin grants when server rejects a replacement', async () => {
    const test = await open(`/admin/roles/${adminId}`)
    test.backend.status = 409; test.backend.code = 'ProtectedAdminGrant'
    test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Xem lại và lưu grant' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Xác nhận lưu grant' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('phải giữ user.manage và role.manage')
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ permissionIds: [], version: 14 })
    expect(writes(test.requests)).toHaveLength(1)
  })
  it('revalidates own permissions and clears caches after a committed grant change', async () => {
    const test = await open(`/admin/roles/${requesterId}`)
    test.backend.nextProfile = { ...admin, permissions: [Permission.UserManage] }
    test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Xem lại và lưu grant' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Xác nhận lưu grant' }))
    await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    expect(test.requests.filter(r => r.url === '/api/v1/users/me')).toHaveLength(2)
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ permissionIds: [permissionId], version: 12 })
  })
  it('reads paged audit only with server capability and renders metadata as text', async () => {
    const test = await open('/admin/roles/audit?page=2')
    test.mount()
    expect(await screen.findByText('Operator')).toBeInTheDocument()
    expect(screen.getByText('Trang 2 / 2 · 21 kết quả')).toBeInTheDocument()
    expect(test.requests.find(r => r.url === '/api/v1/identity/audit-logs')?.params).toEqual({ page: 2, pageSize: 20 })
    expect(screen.getByText('{"DefaultRoleId":"example"}')).toBeInTheDocument()
  })
})
