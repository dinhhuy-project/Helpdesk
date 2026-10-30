import { QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppServices } from '../../../app/providers/app-services'
import { appRoutes } from '../../../app/router/routes'
import { deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import { AuthProvider } from '../providers/auth-provider'
import { expectOnlyCurrentNotificationCount } from '../../../test/notification-cache'

const disposal: (() => void)[] = []
afterEach(() => { cleanup(); for (const dispose of disposal.splice(0)) dispose() })

function response(config: InternalAxiosRequestConfig, data: unknown, status = 200, headers = new AxiosHeaders()) {
  return { config, data, status, statusText: '', headers }
}
function rejected(config: InternalAxiosRequestConfig, status: number, data: unknown, headers = new AxiosHeaders()): never {
  throw new AxiosError('Request failed', 'ERR_BAD_RESPONSE', config, undefined, response(config, data, status, headers))
}

function app(path = '/login', override?: AxiosAdapter) {
  const adapter = vi.fn<AxiosAdapter>(async config => {
    if (config.url === '/api/v1/notifications/unread-count') return response(config, { count: 0 })
    if (override) return override(config)
    if (config.url === '/api/v1/auth/login') {
      expect(config.headers.get('Authorization')).toBeUndefined()
      return response(config, tokenPair())
    }
    if (config.url === '/api/v1/users/me') {
      expect(config.headers.get('Authorization')).toBe('Bearer access-a')
      return response(config, userA)
    }
    if (config.url === '/api/v1/auth/logout' || config.url === '/api/v1/users/me/password') return response(config, undefined, 204)
    throw new Error(`Unexpected test URL: ${config.url}`)
  })
  const services = createAppServices({ baseURL: '/', adapter })
  services.queryClient.setDefaultOptions({ queries: { retry: false } })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  disposal.push(() => { router.dispose(); services.queryClient.clear() })
  const view = render(<QueryClientProvider client={services.queryClient}>
    <AuthProvider session={services.session} client={services.client}><RouterProvider router={router} /></AuthProvider>
  </QueryClientProvider>)
  return { ...services, router, adapter, view }
}

async function fillLogin() {
  const form = await screen.findByRole('form', { name: 'Đăng nhập' })
  await userEvent.type(within(form).getByRole('textbox', { name: 'Email' }), userA.email)
  await userEvent.type(within(form).getByLabelText('Mật khẩu', { exact: false }), 'test-only-password')
  await userEvent.click(within(form).getByRole('button', { name: 'Đăng nhập' }))
}
async function fillPassword(confirmation = 'new-test-only-password') {
  await userEvent.type(screen.getByLabelText('Mật khẩu hiện tại', { exact: false }), 'test-only-password')
  await userEvent.type(screen.getByLabelText(/^Mật khẩu mới/), 'new-test-only-password')
  await userEvent.type(screen.getByLabelText('Xác nhận mật khẩu mới', { exact: false }), confirmation)
  await userEvent.click(screen.getByRole('button', { name: 'Đổi mật khẩu' }))
}

describe('login and account routes over the actual Axios/session wiring', () => {
  it('redirects anonymous deep links without rendering profile data', async () => {
    const { router, adapter } = app('/account/password')
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    expect(router.state.location.pathname).toBe('/login')
    expect(new URLSearchParams(router.state.location.search).get('returnTo')).toBe('/account/password')
    expect(screen.queryByRole('heading', { name: 'Tài khoản của tôi' })).not.toBeInTheDocument()
    expect(adapter).not.toHaveBeenCalled()
  })

  it('logs in, verifies /me and never writes credentials to Query cache or browser storage', async () => {
    const { queryClient, session, adapter } = app()
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    expect(screen.getByText(userA.displayName)).toBeInTheDocument()
    expect(adapter.mock.calls.map(([config]) => config.url)).toEqual(['/api/v1/auth/login', '/api/v1/users/me', '/api/v1/notifications/unread-count'])
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0)
    expect(JSON.stringify(queryClient.getQueryCache().getAll())).not.toContain('test-only-password')
    expect(JSON.stringify(session.store.getState())).not.toContain('access-a')
    expect(localStorage.length + sessionStorage.length).toBe(0)
  })

  it('shows invalid credentials and traceId without trying refresh', async () => {
    const { adapter } = app('/login', async config => rejected(config, 401, {
      title: 'Email hoặc mật khẩu không hợp lệ.', traceId: 'login-trace',
    }))
    await fillLogin()
    expect(await screen.findByRole('alert')).toHaveTextContent('login-trace')
    expect(screen.getByLabelText('Mật khẩu', { exact: false })).toHaveValue('')
    expect(adapter).toHaveBeenCalledTimes(1)
  })

  it('shows rate limiting and disables login until Retry-After', async () => {
    app('/login', async config => rejected(config, 429, { title: 'Quá nhiều lần đăng nhập.' }, new AxiosHeaders({ 'retry-after': '30' })))
    await fillLogin()
    expect(await screen.findByRole('alert')).toHaveTextContent('30 giây')
    expect(screen.getByRole('button', { name: 'Đăng nhập' })).toBeDisabled()
  })

  it('ignores an external returnTo after a successful login', async () => {
    const { router } = app('/login?returnTo=https%3A%2F%2Foutside.test')
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    expect(router.state.location.pathname).toBe('/account')
  })

  it('clears private cache immediately on logout while the remote revoke is still pending', async () => {
    const gate = deferred<void>()
    const { queryClient, session } = app('/login', async config => {
      if (config.url === '/api/v1/auth/logout') { await gate.promise; return response(config, undefined, 204) }
      return response(config, config.url === '/api/v1/auth/login' ? tokenPair() : userA)
    })
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    const key = ['private', session.store.getState().scope, 'tickets']
    queryClient.setQueryData(key, { secret: 'private ticket' })
    await userEvent.click(screen.getByRole('button', { name: 'Đăng xuất' }))
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    expect(queryClient.getQueryData(key)).toBeUndefined()
    expect(session.store.getState().user).toBeNull()
    gate.resolve(undefined)
  })

  it('informs the user when local logout cannot confirm server-side revocation', async () => {
    app('/login', async config => {
      if (config.url === '/api/v1/auth/logout') return rejected(config, 429, {}, new AxiosHeaders({ 'retry-after': '20' }))
      return response(config, config.url === '/api/v1/auth/login' ? tokenPair() : userA)
    })
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    await userEvent.click(screen.getByRole('button', { name: 'Đăng xuất' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('chưa xác nhận thu hồi trên máy chủ')
  })

  it('fails closed on profile 403 without turning it into a refresh loop', async () => {
    let forbidden = false
    const { adapter, session } = app('/login', async config => {
      if (config.url === '/api/v1/users/me' && forbidden) return rejected(config, 403, { title: 'Không có quyền.', traceId: 'forbidden-trace' })
      return response(config, config.url === '/api/v1/auth/login' ? tokenPair() : userA)
    })
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    forbidden = true
    await userEvent.click(screen.getByRole('button', { name: 'Cập nhật thông tin' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('forbidden-trace')
    expect(screen.queryByText(userA.displayName)).not.toBeInTheDocument()
    expect(session.store.getState().status).toBe('authenticated')
    expect(adapter.mock.calls.some(([config]) => config.url === '/api/v1/auth/refresh')).toBe(false)
  })

  it('reloads /me and clears scoped data when current permissions change on focus', async () => {
    let permissions = userA.permissions
    const { queryClient, session } = app('/login', async config => response(config,
      config.url === '/api/v1/auth/login' ? tokenPair() : { ...userA, permissions }))
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    queryClient.setQueryData(['private', session.store.getState().scope], 'old-scoped-data')
    permissions = []
    await act(async () => { fireEvent(window, new Event('focus')) })
    await screen.findByText('Quyền hiện tại (0)')
    expectOnlyCurrentNotificationCount(queryClient, session.store.getState().scope)
  })

  it('starts anonymous after constructing a new app instance (hard reload)', async () => {
    const first = app()
    await fillLogin()
    await screen.findByRole('heading', { name: 'Tài khoản của tôi' })
    first.view.unmount()
    const second = app('/account')
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    expect(second.session.store.getState().user).toBeNull()
    expect(second.adapter).not.toHaveBeenCalled()
  })
})

describe('self-service password form', () => {
  it('changes password, sends only the backend DTO, and redirects to login after revoking the session', async () => {
    const { adapter, session, queryClient } = app('/login?returnTo=%2Faccount%2Fpassword')
    await fillLogin()
    await screen.findByRole('heading', { name: 'Đổi mật khẩu' })
    await fillPassword()
    await screen.findByRole('heading', { name: 'Đăng nhập' })
    expect(screen.getByRole('status')).toHaveTextContent('Đổi mật khẩu thành công')
    expect(session.store.getState().status).toBe('anonymous')
    const request = adapter.mock.calls.find(([config]) => config.url === '/api/v1/users/me/password')?.[0]
    expect(JSON.parse(request?.data as string)).toEqual({ currentPassword: 'test-only-password', newPassword: 'new-test-only-password' })
    expect(request?.headers.get('Authorization')).toBe('Bearer access-a')
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0)
  })

  it('does not submit a mismatched confirmation', async () => {
    const { adapter } = app('/login?returnTo=%2Faccount%2Fpassword')
    await fillLogin()
    await screen.findByRole('heading', { name: 'Đổi mật khẩu' })
    await fillPassword('different-test-password')
    expect(await screen.findByText('Mật khẩu xác nhận không khớp.')).toBeInTheDocument()
    expect(adapter.mock.calls.some(([config]) => config.url === '/api/v1/users/me/password')).toBe(false)
  })

  it('maps an invalid current password to its field and keeps the user signed in', async () => {
    const { session } = app('/login?returnTo=%2Faccount%2Fpassword', async config => {
      if (config.url === '/api/v1/users/me/password') return rejected(config, 400, { title: 'Mật khẩu không đúng.', code: 'InvalidCurrentPassword' })
      return response(config, config.url === '/api/v1/auth/login' ? tokenPair() : userA)
    })
    await fillLogin()
    await screen.findByRole('heading', { name: 'Đổi mật khẩu' })
    await fillPassword()
    expect(await screen.findByText('Mật khẩu hiện tại không đúng.')).toBeInTheDocument()
    expect(session.store.getState().status).toBe('authenticated')
  })
})
