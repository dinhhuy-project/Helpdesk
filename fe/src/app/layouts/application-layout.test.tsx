import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppServices } from '../providers/app-services'
import { appRoutes } from '../router/routes'
import { AuthProvider } from '../../features/auth/providers/auth-provider'
import { Permission } from '../../features/auth/lib/permissions'
import { deferred, tokenPair, userA } from '../../test/auth-fixtures'
import type { UserProfile } from '../../features/auth/types/auth'
import { RequireSession } from '../../features/auth/components/require-session'
import { RequirePermission } from '../../features/auth/components/require-permission'

const disposals: (() => void)[] = []
afterEach(() => { cleanup(); for (const dispose of disposals.splice(0)) dispose() })

function fixture(initial: UserProfile) {
  const backend: { user: UserProfile; status: number; gate?: Promise<UserProfile> } = { user: initial, status: 200 }
  const adapter: AxiosAdapter = async (config: InternalAxiosRequestConfig) => {
    const response = { config, status: 200, statusText: '', headers: new AxiosHeaders(), data: undefined as unknown }
    if (config.url === '/api/v1/auth/login') return { ...response, data: tokenPair('shell', backend.user) }
    if (config.url === '/api/v1/users/me') {
      if (backend.status !== 200) throw new AxiosError('Rejected', 'ERR_BAD_RESPONSE', config, undefined,
        { ...response, status: backend.status, data: { title: 'Không xác nhận được tài khoản.', traceId: 'profile-test' } })
      return { ...response, data: backend.gate ? await backend.gate : backend.user }
    }
    if (config.url === '/api/v1/auth/logout') return { ...response, status: 204 }
    throw new Error('Unexpected fixture route')
  }
  return { backend, services: createAppServices({ baseURL: '/', adapter }) }
}

async function openShell(user = userA, path = '/account') {
  const { backend, services } = fixture(user)
  await services.session.login({ email: user.email, password: 'test-only-password' })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposals.push(() => { router.dispose(); services.queryClient.clear() })
  render(<QueryClientProvider client={services.queryClient}>
    <AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider>
  </QueryClientProvider>)
  return { backend, services, router }
}

describe('application shell, permissions and error pages', () => {
  it('shows current multi-role labels and the permission union without granting user administration', async () => {
    await openShell({ ...userA, roles: ['Requester', 'TeamLeader'], permissions: [Permission.TicketCreate, Permission.TeamManage] })
    const navigation = screen.getByRole('navigation', { name: 'Điều hướng chính' })
    expect(within(navigation).getByText('Tạo yêu cầu')).toBeInTheDocument()
    expect(within(navigation).getByText('Nhóm hỗ trợ')).toBeInTheDocument()
    expect(within(navigation).queryByText('Tài khoản người dùng')).not.toBeInTheDocument()
    expect(screen.getByText('Người yêu cầu · Trưởng nhóm hỗ trợ')).toBeInTheDocument()
    expect(within(navigation).getByRole('link', { name: 'Tài khoản' })).toHaveAttribute('aria-current', 'page')
  })

  it('does not bypass missing permissions for a SystemAdmin role name', async () => {
    await openShell({ ...userA, roles: ['SystemAdmin'], permissions: [] }, '/admin/users')
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
    expect(screen.queryByText('Tài khoản người dùng')).not.toBeInTheDocument()
    expect(document.title).toBe('Không có quyền truy cập | Helpdesk')
  })

  it('keeps future modules disabled and returns 404 after their guard accepts a deep link', async () => {
    await openShell({ ...userA, permissions: [Permission.UserManage] }, '/admin/users')
    expect(screen.getByRole('heading', { name: 'Không tìm thấy trang' })).toBeInTheDocument()
    expect(within(screen.getByRole('navigation', { name: 'Điều hướng chính' }))
      .getByText('Tài khoản người dùng').closest('[aria-disabled]')).toHaveAttribute('aria-disabled', 'true')
    expect(screen.queryByRole('link', { name: 'Tài khoản người dùng' })).not.toBeInTheDocument()
  })

  it('does not require read permission just to authorize the future create route', async () => {
    await openShell({ ...userA, permissions: [Permission.TicketCreate] }, '/tickets/new')
    expect(screen.getByRole('heading', { name: 'Không tìm thấy trang' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Không có quyền truy cập' })).not.toBeInTheDocument()
  })

  it('removes navigation and blocks the same URL after current grants are revoked', async () => {
    const { backend, services } = await openShell({ ...userA, permissions: [Permission.UserManage] }, '/admin/users')
    backend.user = { ...userA, permissions: [] }
    await act(async () => { await services.session.refreshProfile() })
    expect(screen.queryByText('Tài khoản người dùng')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument()
  })

  it('hides profile content and permission navigation during revalidation and after a profile error', async () => {
    const { backend, services } = await openShell({ ...userA, permissions: [Permission.UserManage] })
    const gate = deferred<UserProfile>()
    backend.gate = gate.promise
    let pending: Promise<void> | undefined
    act(() => { pending = services.session.refreshProfile().catch(() => {}) })
    expect(screen.queryByText(userA.displayName)).not.toBeInTheDocument()
    expect(screen.queryByText('Tài khoản người dùng')).not.toBeInTheDocument()
    await act(async () => { gate.reject(new Error('No response')); await pending })
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Tài khoản của tôi' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Đăng xuất' })).toBeInTheDocument()
  })

  it('opens a modal menu and closes it on navigation while moving focus to the new page', async () => {
    await openShell()
    const button = screen.getByRole('button', { name: 'Mở menu điều hướng' })
    await userEvent.click(button)
    expect(button).toHaveAttribute('aria-expanded', 'true')
    const dialog = screen.getByRole('dialog', { name: 'Điều hướng' })
    expect(document.body.style.overflow).toBe('hidden')
    await userEvent.click(within(dialog).getByRole('link', { name: 'Bảo mật' }))
    await screen.findByRole('heading', { name: 'Đổi mật khẩu' })
    expect(dialog).not.toHaveAttribute('open')
    expect(document.body.style.overflow).not.toBe('hidden')
    await vi.waitFor(() => expect(document.activeElement).toBe(document.getElementById('main-content')))
  })

  it('closes the menu on Escape cancellation and restores page scrolling', async () => {
    await openShell()
    await userEvent.click(screen.getByRole('button', { name: 'Mở menu điều hướng' }))
    const dialog = screen.getByRole('dialog', { name: 'Điều hướng' })
    fireEvent(dialog, new Event('cancel', { cancelable: true }))
    expect(dialog).not.toHaveAttribute('open')
    expect(document.body.style.overflow).not.toBe('hidden')
  })

  it('preserves password drafts during a successful background profile check on window focus', async () => {
    const { backend, services } = await openShell(userA, '/account/password')
    await userEvent.type(screen.getByLabelText(/^Mật khẩu hiện tại/), 'current-password-draft')
    const gate = deferred<UserProfile>()
    backend.gate = gate.promise
    act(() => { fireEvent(window, new Event('focus')) })
    expect(services.session.store.getState().profilePending).toBe(false)
    expect(screen.getByLabelText(/^Mật khẩu hiện tại/)).toHaveValue('current-password-draft')
    await act(async () => { gate.resolve(backend.user); await gate.promise })
    expect(screen.getByLabelText(/^Mật khẩu hiện tại/)).toHaveValue('current-password-draft')
  })
})

describe('guard child rendering', () => {
  it('does not mount protected content while login/profile confirmation is pending', async () => {
    const { backend, services } = fixture({ ...userA, permissions: [Permission.UserManage] })
    const gate = deferred<UserProfile>()
    backend.gate = gate.promise
    const login = services.session.login({ email: userA.email, password: 'test-only-password' })
    const mounted = vi.fn()
    function PrivateContent() { mounted(); return <p>Protected content</p> }
    const router = createMemoryRouter([{ element: <RequireSession />, children: [
      { element: <RequirePermission rule={{ allOf: [Permission.UserManage] }} />, children: [
        { path: '/', element: <PrivateContent /> },
      ] },
    ] }])
    disposals.push(() => { router.dispose(); services.queryClient.clear() })
    render(<AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider>)
    expect(mounted).not.toHaveBeenCalled()
    await act(async () => { gate.resolve(backend.user); await login })
    expect(await screen.findByText('Protected content')).toBeInTheDocument()
  })
})
