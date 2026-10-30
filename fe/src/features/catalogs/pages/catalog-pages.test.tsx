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
import { Permission } from '../../auth/lib/permissions'
import type { UserProfile } from '../../auth/types/auth'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import { catalogKeys } from '../hooks/use-catalogs'
import type { CatalogRecord, Department, SupportTeam, TeamAccess, TeamMember, TicketCategory } from '../types/catalog'

const departmentId = '81000000-0000-0000-0000-000000000001'
const teamId = '81000000-0000-0000-0000-000000000002'
const categoryId = '81000000-0000-0000-0000-000000000003'
const childId = '81000000-0000-0000-0000-000000000004'
const memberId = '81000000-0000-0000-0000-000000000005'
const leaderId = '81000000-0000-0000-0000-000000000006'
const candidateId = '81000000-0000-0000-0000-000000000007'
const department: Department = { id: departmentId, name: 'Phòng kỹ thuật', description: null, isActive: true, createdAt: '2026-10-29T00:00:00Z', updatedAt: null, version: 41 }
const team: SupportTeam = { ...department, id: teamId, name: 'Nhóm hỗ trợ thử nghiệm', departmentId, departmentName: department.name, departmentIsActive: true }
const category: TicketCategory = { ...department, id: categoryId, name: 'Máy tính', parentId: null, parentName: null, hasChildren: true, isAvailable: true, canActivate: true }
const child: TicketCategory = { ...category, id: childId, parentId: categoryId, parentName: category.name, name: 'Laptop', hasChildren: false }
const member: TeamMember = { userId: memberId, displayName: 'Thành viên thường', userIsActive: true, isActive: true, isLeader: false, joinedAt: department.createdAt, updatedAt: null, version: 71, canBeLeader: false }
const leader: TeamMember = { ...member, userId: leaderId, displayName: 'Trưởng nhóm hiện tại', isLeader: true, canBeLeader: true }
const admin: UserProfile = { ...userA, roles: ['SystemAdmin'], permissions: [Permission.DepartmentManage, Permission.TeamManage, Permission.CategoryManage] }
const disposals: (() => void)[] = []
afterEach(() => { cleanup(); for (const dispose of disposals.splice(0)) dispose() })
async function open(path = '/admin/departments', profile = admin) {
  const requests: InternalAxiosRequestConfig[] = []
  const backend: { profile: UserProfile; department: Department; team: SupportTeam; category: TicketCategory; child: TicketCategory; members: TeamMember[]; access: TeamAccess
    writeStatus: number; writeCode?: string; readStatus: number; accessStatus: number; detailStatus: number; total: number; writeGate?: Promise<CatalogRecord>; listGate?: Promise<void> } = {
    profile, department: structuredClone(department), team: structuredClone(team), category: structuredClone(category), child: structuredClone(child), members: structuredClone([member, leader]),
    access: { canCreate: true, canAddMembers: true, canChangeStatus: true, canManageLeadership: true }, writeStatus: 200, readStatus: 200, accessStatus: 200, detailStatus: 200, total: 1,
  }
  const page = <T,>(items: T[], config: InternalAxiosRequestConfig, totalCount = items.length) => ({ items, totalCount, page: Number(config.params?.page ?? 1), pageSize: Number(config.params?.pageSize ?? 20) })
  const adapter: AxiosAdapter = async config => {
    requests.push(config)
    const url = config.url ?? ''
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    const fail = (status: number, code?: string) => {
      if (status >= 400) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined,
        { ...response, status, headers: new AxiosHeaders({ 'Retry-After': '30' }), data: { title: 'Rejected', code, traceId: 'catalog-trace' } })
    }
    if (url === '/api/v1/auth/login') return { ...response, data: tokenPair('catalogs', backend.profile) }
    if (url === '/api/v1/users/me') return { ...response, data: backend.profile }
    if (url === '/api/v1/auth/logout') return { ...response, status: 204 }
    if (url.endsWith('/administration-access')) { fail(backend.accessStatus); return { ...response, data: backend.access } }
    if (config.method === 'get') {
      fail(backend.readStatus)
      if (url.endsWith('/department-lookup')) return { ...response, data: page([{ id: departmentId, name: department.name }], config, 21) }
      if (url.endsWith('/member-candidates')) return { ...response, data: page([{ id: candidateId, displayName: 'Ứng viên hợp lệ', departmentName: department.name }], config, 21) }
      if (url.endsWith('/members')) return { ...response, data: page(backend.members, config, backend.members.length) }
      if (['/api/v1/departments', '/api/v1/support-teams', '/api/v1/ticket-categories'].includes(url)) {
        if (backend.listGate) await backend.listGate
        const items = url.endsWith('departments') ? [backend.department] : url.endsWith('support-teams') ? [backend.team]
          : config.params?.parentId ? [backend.child] : [backend.category]
        return { ...response, data: page(items, config, backend.total) }
      }
      fail(backend.detailStatus)
      return { ...response, data: url.includes('support-teams') ? backend.team : url.includes('ticket-categories') ? url.endsWith(childId) ? backend.child : backend.category : backend.department }
    }
    if (backend.writeGate) return { ...response, data: await backend.writeGate }
    fail(backend.writeStatus, backend.writeCode)
    const body = config.data ? JSON.parse(config.data) as Record<string, unknown> : {}
    if (url.includes('/members')) {
      const userId = url.endsWith('/members') ? String(body.userId) : url.split('/members/')[1]?.split('/')[0]
      let current = backend.members.find(item => item.userId === userId)
      if (!current) { current = { ...member, userId: String(userId), displayName: 'Ứng viên hợp lệ' }; backend.members.push(current) }
      current.version++
      if (config.method === 'delete') current.isActive = false
      if (typeof body.isActive === 'boolean') current.isActive = body.isActive
      if (typeof body.isLeader === 'boolean') current.isLeader = body.isLeader
      backend.team = { ...backend.team, version: backend.team.version + 1 }
      return { ...response, status: config.method === 'delete' ? 204 : config.method === 'post' ? 201 : 200, data: config.method === 'delete' ? undefined : { team: backend.team, member: current } }
    }
    const key = url.includes('support-teams') ? 'team' : url.includes('ticket-categories') ? 'category' : 'department'
    const resource = backend[key]
    const updated = { ...resource, version: resource.version + 1, ...(typeof body.name === 'string' ? { name: body.name } : {}),
      ...('description' in body ? { description: typeof body.description === 'string' ? body.description : null } : {}),
      ...(typeof body.isActive === 'boolean' ? { isActive: body.isActive } : {}),
      ...('parentId' in body ? { parentId: typeof body.parentId === 'string' ? body.parentId : null } : {}) }
    if (key === 'team') backend.team = { ...backend.team, ...updated }
    else if (key === 'category') backend.category = { ...backend.category, ...updated }
    else backend.department = updated
    return { ...response, status: config.method === 'post' ? 201 : 200, data: updated }
  }
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  await services.session.login({ email: profile.email, password: 'test-only' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  const mount = () => render(<QueryClientProvider client={services.queryClient}><AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>)
  return { backend, requests, services, router, mount }
}
const writes = (requests: InternalAxiosRequestConfig[]) => requests.filter(r => r.method !== 'get' && r.url !== '/api/v1/auth/login' && r.url !== '/api/v1/auth/logout')
async function saveName(name: string) {
  const field = await screen.findByRole('textbox', { name: 'Tên' })
  await userEvent.clear(field); await userEvent.type(field, name)
  await userEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
}
async function confirmMember() {
  await userEvent.click(within(screen.getByRole('dialog', { name: 'Xác nhận thay đổi thành viên' })).getByRole('button', { name: 'Xác nhận thay đổi thành viên' }))
}

describe('Catalog permission, scoped reads and server queries', () => {
  it.each(['/admin/departments', '/admin/departments/new', `/admin/departments/${departmentId}`, '/admin/teams', '/admin/teams/new', `/admin/teams/${teamId}`, '/admin/categories', '/admin/categories/new'])('rejects role names without permission at %s', async path => {
    const test = await open(path, { ...userA, roles: ['SystemAdmin', 'TeamLeader'], permissions: [] })
    test.mount()
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(test.requests.filter(r => r.url !== '/api/v1/auth/login' && r.url !== '/api/v1/users/me' && r.url !== '/api/v1/notifications/unread-count')).toHaveLength(0)
  })
  it('filters/sorts/pages at the server and restores the URL on back', async () => {
    const test = await open('/admin/departments?page=2&search=IT&isActive=false&sortBy=createdAt&descending=true')
    test.backend.total = 21; test.mount()
    await screen.findByRole('link', { name: department.name })
    expect(test.requests.find(r => r.url === '/api/v1/departments')?.params).toMatchObject({ page: 2, search: 'IT', isActive: false, sortBy: 'createdAt', descending: true })
    expect(screen.getByText('Trang 2 / 2 · 21 kết quả')).toBeInTheDocument()
    await userEvent.clear(screen.getByRole('searchbox')); await userEvent.type(screen.getByRole('searchbox'), 'Mới')
    await userEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' }))
    await waitFor(() => expect(test.requests.filter(r => r.url === '/api/v1/departments').at(-1)?.params).toMatchObject({ page: 1, search: 'Mới' }))
    await act(async () => { await test.router.navigate(-1) })
    expect(screen.getByRole('searchbox')).toHaveValue('IT')
  })
  it.each(['/admin/departments?page=0', '/admin/teams?departmentId=invalid', '/admin/categories?rootsOnly=true&parentId=' + categoryId, '/admin/categories?page=1&page=2'])('rejects an invalid query at %s', async path => {
    const test = await open(path); test.mount()
    await screen.findByRole('heading', { name: 'Bộ lọc chưa hợp lệ' })
    expect(test.requests.some(r => r.url?.startsWith('/api/v1/support-teams') || r.url === '/api/v1/departments' || r.url?.startsWith('/api/v1/ticket-categories'))).toBe(false)
  })
  it('browses categories by hasChildren and keeps branch pagination in URL', async () => {
    const test = await open('/admin/categories'); test.mount()
    await userEvent.click(await screen.findByRole('link', { name: `Xem nhánh ${category.name}` }))
    await screen.findByRole('link', { name: child.name })
    expect(test.requests.filter(r => r.url === '/api/v1/ticket-categories').at(-1)?.params).toMatchObject({ parentId: categoryId, rootsOnly: false })
    expect(test.router.state.location.search).toContain(`parentId=${categoryId}`)
    expect(screen.getByRole('navigation', { name: 'Cây danh mục' })).toHaveTextContent(category.name)
  })
  it('uses server capability to deny team creation even for a SystemAdmin role label', async () => {
    const test = await open('/admin/teams/new', { ...admin, permissions: [Permission.TeamManage] })
    test.backend.access.canCreate = false; test.mount()
    await screen.findByRole('heading', { name: 'Không có quyền truy cập' })
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(test.requests.some(r => r.url?.endsWith('/department-lookup'))).toBe(false)
  })
  it.each([403,404])('checks parent scope before member names or counts on HTTP %i', async status => {
    const test = await open(`/admin/teams/${teamId}`)
    test.backend.accessStatus = status; test.mount()
    await screen.findByRole('heading', { name: status === 403 ? 'Không có quyền truy cập' : 'Không tìm thấy dữ liệu quản trị' })
    expect(test.requests.some(r => r.url?.endsWith('/members') || r.url?.endsWith('/member-candidates') || r.url === `/api/v1/support-teams/${teamId}`)).toBe(false)
  })
  it('clears scope cache and ignores a late list response after current grant removal', async () => {
    const test = await open(); const gate = deferred<void>(); test.backend.listGate = gate.promise; test.mount()
    test.backend.profile = { ...admin, permissions: [] }
    await act(async () => { await test.services.session.refreshProfile() })
    await act(async () => { gate.resolve(); await gate.promise })
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: department.name })).not.toBeInTheDocument()
    expectOnlyCurrentNotificationCount(test.services.queryClient, test.services.session.store.getState().scope)
  })
})

describe('Catalog forms, history, concurrency and uncertain writes', () => {
  it('posts only department DTO and opens the confirmed detail', async () => {
    const test = await open('/admin/departments/new'); test.mount()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Tên' }), 'Phòng mới')
    await userEvent.type(screen.getByRole('textbox', { name: 'Mô tả' }), 'Nội dung mô tả')
    await userEvent.click(screen.getByRole('button', { name: 'Tạo mới' }))
    await screen.findByRole('heading', { name: 'Phòng mới' })
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ name: 'Phòng mới', description: 'Nội dung mô tả' })
    expect(test.router.state.location.pathname).toBe(`/admin/departments/${departmentId}`)
    expect(test.services.queryClient.getMutationCache().getAll()).toHaveLength(0)
  })
  it('selects a paged team department without user.manage and prevents Enter in lookup from submitting creation', async () => {
    const test = await open('/admin/teams/new', { ...userA, roles: ['Custom'], permissions: [Permission.TeamManage] }); test.mount()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Tên' }), 'Team mới')
    await userEvent.click(screen.getByRole('button', { name: 'Chọn phòng ban hoạt động' }))
    const dialog = screen.getByRole('dialog', { name: 'Chọn phòng ban hoạt động' })
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Tìm phòng ban' }), 'Phòng{Enter}')
    await waitFor(() => expect(test.requests.filter(r => r.url?.endsWith('/department-lookup')).at(-1)?.params.search).toBe('Phòng'))
    expect(writes(test.requests)).toHaveLength(0)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Trang sau' }))
    await userEvent.click(await within(dialog).findByRole('button', { name: department.name }))
    await userEvent.click(screen.getByRole('button', { name: 'Tạo mới' }))
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ name: 'Team mới', description: null, departmentId })
    expect(test.requests.some(r => r.url === '/api/v1/users' || r.url === '/api/v1/departments/lookup')).toBe(false)
  })
  it('keeps department immutable when updating team metadata', async () => {
    const test = await open(`/admin/teams/${teamId}`); test.mount(); await saveName('Tên nhóm mới')
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ name: 'Tên nhóm mới', description: null, version: 41 })
    expect(screen.queryByRole('button', { name: 'Chọn phòng ban hoạt động' })).not.toBeInTheDocument()
  })
  it('preserves name draft and original version on background updates and 409 until explicit reload', async () => {
    const test = await open(`/admin/departments/${departmentId}`); test.mount()
    const field = await screen.findByRole('textbox', { name: 'Tên' })
    await userEvent.clear(field); await userEvent.type(field, 'Bản nhập')
    test.backend.department = { ...department, version: 81, name: 'Bản server mới' }
    await act(async () => { test.services.queryClient.setQueryData(catalogKeys.detail(test.services.session.store.getState().scope, 'departments', departmentId), test.backend.department) })
    expect(field).toHaveValue('Bản nhập')
    test.backend.writeStatus = 409; test.backend.writeCode = 'VersionConflict'
    await userEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Bản nhập được giữ lại')
    expect(JSON.parse(writes(test.requests)[0]?.data).version).toBe(41)
    expect(screen.getByRole('button', { name: 'Lưu thay đổi' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Tải lại dữ liệu' }))
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Tên' })).toHaveValue('Bản server mới'))
    expect(screen.getByRole('button', { name: 'Lưu thay đổi' })).toBeEnabled()
  })
  it('changes parent through category administration without ticket.create, using explicit null to return to root', async () => {
    const test = await open(`/admin/categories/${categoryId}`, { ...userA, permissions: [Permission.CategoryManage] })
    test.backend.category = { ...category, parentId: childId, parentName: 'Cha lịch sử' }; test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Chuyển về danh mục gốc' }))
    await userEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ name: category.name, description: null, parentId: null, version: 41 })
    expect(test.requests.some(r => r.url?.endsWith('/lookup'))).toBe(false)
  })
  it('blocks selecting itself/its browsed subtree as parent and displays server cycle rejection without discarding drafts', async () => {
    const test = await open(`/admin/categories/${categoryId}`)
    test.backend.writeStatus = 400; test.backend.writeCode = 'CategoryCycle'; test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Chọn danh mục cha' }))
    const dialog = screen.getByRole('dialog', { name: 'Chọn danh mục cha khả dụng' })
    expect(await within(dialog).findByRole('button', { name: category.name })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: `Mở nhánh ${category.name}` })).toBeDisabled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Đóng' }))
    await saveName('Giữ bản nhập khi vòng lặp')
    expect(await screen.findByRole('alert')).toHaveTextContent('chuyển vào nhánh con')
    expect(screen.getByRole('textbox', { name: 'Tên' })).toHaveValue('Giữ bản nhập khi vòng lặp')
    expect(writes(test.requests)).toHaveLength(1)
  })
  it('keeps historical inactive parent for a rename and disables activation when ancestors are unavailable', async () => {
    const test = await open(`/admin/categories/${categoryId}`)
    test.backend.category = { ...category, isActive: false, parentId: childId, parentName: 'Cha đã tắt', isAvailable: false, canActivate: false }; test.mount()
    await screen.findByText('Cha đã tắt')
    expect(screen.getByRole('button', { name: 'Kích hoạt' })).toBeDisabled()
    await saveName('Tên lịch sử mới')
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data).parentId).toBe(childId)
  })
  it.each(['NameInUse', 'InvalidParent'])('shows %s and traceId without replaying writes', async code => {
    const test = await open(`/admin/categories/${categoryId}`)
    test.backend.writeStatus = code === 'NameInUse' ? 409 : 400; test.backend.writeCode = code; test.mount()
    await saveName('Tên cần kiểm tra')
    expect(await screen.findByRole('alert')).toHaveTextContent('catalog-trace')
    expect(writes(test.requests)).toHaveLength(1)
    expect(test.requests.some(r => r.url === '/api/v1/auth/refresh')).toBe(false)
  })
  it('requires confirmation and writes only a status/version, preserving historical resources', async () => {
    const test = await open(`/admin/departments/${departmentId}`); test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Vô hiệu hóa' }))
    const dialog = screen.getByRole('dialog', { name: `Vô hiệu hóa: ${department.name}` })
    expect(dialog).toHaveTextContent('Giữ liên kết user')
    expect(writes(test.requests)).toHaveLength(0)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận đổi trạng thái' }))
    await screen.findByRole('button', { name: 'Kích hoạt' })
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ isActive: false, version: 41 })
    expect(writes(test.requests)[0]?.method).toBe('put')
  })
  it('locks uncertain creation instead of retrying POST after timeout', async () => {
    const test = await open('/admin/departments/new'); const gate = deferred<CatalogRecord>(); test.backend.writeGate = gate.promise; test.mount()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Tên' }), 'Tạo chưa rõ')
    await userEvent.dblClick(screen.getByRole('button', { name: 'Tạo mới' }))
    await act(async () => { gate.reject(new AxiosError('timeout', 'ECONNABORTED')); await gate.promise.catch(() => {}) })
    expect(await screen.findByRole('alert')).toHaveTextContent('Chưa xác định được kết quả')
    expect(screen.getByRole('button', { name: 'Tạo mới' })).toBeDisabled()
    expect(writes(test.requests)).toHaveLength(1)
  })
  it('does not publish a late create result after logout', async () => {
    const test = await open('/admin/departments/new'); const gate = deferred<CatalogRecord>(); test.backend.writeGate = gate.promise; test.mount()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Tên' }), 'Tạo muộn')
    await userEvent.click(screen.getByRole('button', { name: 'Tạo mới' }))
    await act(async () => { await test.services.session.logout() })
    await act(async () => { gate.resolve(department); await gate.promise })
    expect(test.router.state.location.pathname).toBe('/login')
    expect(test.services.queryClient.getQueryCache().getAll()).toHaveLength(0)
  })
})

describe('Membership and leadership boundaries', () => {
  it('does not infer leadership administration from a SystemAdmin label and hides leader mutations for delegates', async () => {
    const test = await open(`/admin/teams/${teamId}`, { ...userA, roles: ['SystemAdmin'], permissions: [Permission.TeamManage] })
    test.backend.access = { canCreate: false, canChangeStatus: false, canManageLeadership: false, canAddMembers: true }; test.mount()
    await screen.findByRole('table', { name: 'Thành viên nhóm hỗ trợ' })
    expect(screen.queryByRole('button', { name: 'Gỡ trưởng nhóm' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Chỉ định trưởng nhóm' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Ngừng tham gia' })).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Vô hiệu hóa' })).not.toBeInTheDocument()
  })
  it('adds from team-scoped candidate lookup and sends only userId/teamVersion', async () => {
    const test = await open(`/admin/teams/${teamId}`); test.mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Thêm thành viên' }))
    const dialog = screen.getByRole('dialog', { name: 'Chọn người dùng hoạt động' })
    await userEvent.click(await within(dialog).findByRole('button', { name: /Ứng viên hợp lệ/ }))
    await confirmMember()
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ userId: candidateId, teamVersion: 41 })
    expect(test.requests.some(r => r.url === '/api/v1/users')).toBe(false)
  })
  it('pins both versions, adopts committed team version, and preserves an unsaved metadata draft', async () => {
    const test = await open(`/admin/teams/${teamId}`); test.mount()
    const field = await screen.findByRole('textbox', { name: 'Tên' }); await userEvent.clear(field); await userEvent.type(field, 'Draft tên nhóm')
    await userEvent.click((await screen.findAllByRole('button', { name: 'Ngừng tham gia' }))[0]!)
    await confirmMember()
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ isActive: false, version: 71, teamVersion: 41 })
    await screen.findByRole('button', { name: 'Kích hoạt lại' })
    expect(field).toHaveValue('Draft tên nhóm')
    await userEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
    await waitFor(() => expect(writes(test.requests)).toHaveLength(2))
    expect(JSON.parse(writes(test.requests)[1]?.data).version).toBe(42)
  })
  it('DELETE removes softly with two query versions, then reloads the new team version without replaying DELETE', async () => {
    const test = await open(`/admin/teams/${teamId}`); test.mount()
    await userEvent.click((await screen.findAllByRole('button', { name: 'Gỡ khỏi nhóm' }))[0]!)
    await confirmMember()
    await screen.findByRole('button', { name: 'Kích hoạt lại' })
    const request = writes(test.requests)[0]
    expect(request?.method).toBe('delete')
    expect(request?.params).toEqual({ version: 71, teamVersion: 41 })
    expect(request?.data).toBeUndefined()
    expect(test.requests.filter(r => r.url === `/api/v1/support-teams/${teamId}` && r.method === 'get').length).toBeGreaterThanOrEqual(2)
    expect(writes(test.requests)).toHaveLength(1)
  })
  it('treats DELETE as committed even when its follow-up read fails and locks further writes', async () => {
    const test = await open(`/admin/teams/${teamId}`); test.mount()
    await userEvent.click((await screen.findAllByRole('button', { name: 'Gỡ khỏi nhóm' }))[0]!)
    test.backend.detailStatus = 503
    await confirmMember()
    await screen.findByText(/Thao tác gỡ đã thành công/)
    expect(screen.getByRole('button', { name: 'Lưu thay đổi' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Thêm thành viên' })).toBeDisabled()
    expect(writes(test.requests)).toHaveLength(1)
  })
  it('disables ineligible promotion, sends leadership/version and never assigns a global role', async () => {
    const test = await open(`/admin/teams/${teamId}`); test.mount()
    expect(await screen.findByRole('button', { name: 'Chỉ định trưởng nhóm' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Gỡ trưởng nhóm' }))
    await confirmMember()
    await waitFor(() => expect(writes(test.requests)).toHaveLength(1))
    expect(JSON.parse(writes(test.requests)[0]?.data)).toEqual({ isLeader: false, version: 71, teamVersion: 41 })
    expect(writes(test.requests)[0]?.url).toContain('/leadership')
    expect(test.requests.some(r => r.url?.endsWith('/roles'))).toBe(false)
  })
  it('shows invalid dual-version conflict without refreshing/resubmitting the mutation', async () => {
    const test = await open(`/admin/teams/${teamId}`)
    test.backend.writeStatus = 409; test.backend.writeCode = 'VersionConflict'; test.mount()
    await userEvent.click((await screen.findAllByRole('button', { name: 'Ngừng tham gia' }))[0]!)
    await confirmMember()
    expect(await screen.findByRole('alert')).toHaveTextContent('tải lại và kiểm tra')
    expect(writes(test.requests)).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Thêm thành viên' })).toBeDisabled()
  })
  it('keeps inactive team/membership history while blocking new membership and inactive-user reactivation', async () => {
    const test = await open(`/admin/teams/${teamId}`)
    test.backend.team.departmentIsActive = false; test.backend.access.canAddMembers = false
    test.backend.members[0] = { ...member, isActive: false, userIsActive: false }; test.mount()
    expect(await screen.findByRole('button', { name: 'Thêm thành viên' })).toBeDisabled()
    expect(await screen.findByRole('button', { name: 'Kích hoạt lại' })).toBeDisabled()
    expect(screen.getByText('Tài khoản đã vô hiệu hóa')).toBeInTheDocument()
    expect(screen.getAllByText('Phòng ban đã vô hiệu hóa').length).toBeGreaterThan(0)
  })
  it('keeps member filters on URL and suppresses invalid member pagination requests', async () => {
    const test = await open(`/admin/teams/${teamId}?memberPage=0`); test.mount()
    await screen.findByRole('heading', { name: 'Bộ lọc thành viên chưa hợp lệ' })
    expect(test.requests.some(r => r.url?.endsWith('/members'))).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Xóa bộ lọc thành viên' }))
    await screen.findByRole('table', { name: 'Thành viên nhóm hỗ trợ' })
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Trạng thái tham gia' }), 'false')
    await waitFor(() => expect(test.requests.filter(r => r.url?.endsWith('/members')).at(-1)?.params.isActive).toBe(false))
    expect(test.router.state.location.search).toContain('memberIsActive=false')
  })
})
