import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../shared/api/api-error'
import { createMockAuthApi, deferred, tokenPair, userA, userB } from '../../../test/auth-fixtures'
import type { LoginResponse, UserProfile } from '../types/auth'
import { createAuthSession } from './auth-session'

const loginCommand = { email: userA.email, password: 'test-only-password' }
const passwordCommand = { currentPassword: 'test-only-password', newPassword: 'new-test-only-password' }
const denied = () => new ApiError('Session invalid', 'http', 401)

async function signedIn() {
  const api = createMockAuthApi()
  const onScopeChange = vi.fn()
  const session = createAuthSession({ api, onScopeChange })
  await session.login(loginCommand)
  return { api, onScopeChange, session }
}

afterEach(() => { vi.useRealTimers() })

describe('authentication and memory-only identity', () => {
  it('ends a confirmed revoked current session without making a revoke/logout request', async () => {
    const { session, api, onScopeChange } = await signedIn()
    const scope = session.store.getState().scope
    expect(scope).not.toBeNull()
    session.endRevokedSession(scope ?? '')
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', user: null, reason: 'revoked' })
    expect(api.logout).not.toHaveBeenCalled()
    expect(onScopeChange).toHaveBeenCalledTimes(2)
  })

  it('does not let a late self-deactivation receipt end a newer login', async () => {
    const { session } = await signedIn()
    const previousScope = session.store.getState().scope
    await session.login(loginCommand)
    session.endRevokedSession(previousScope ?? '')
    expect(session.store.getState().status).toBe('authenticated')
    expect(session.store.getState().scope).not.toBe(previousScope)
  })
  it('does not publish an authenticated user before /me confirms it', async () => {
    const api = createMockAuthApi()
    const me = deferred<UserProfile>()
    api.me.mockReturnValue(me.promise)
    const session = createAuthSession({ api, onScopeChange: vi.fn() })
    const login = session.login(loginCommand)
    await vi.waitFor(() => expect(api.me).toHaveBeenCalled())
    expect(session.store.getState()).toMatchObject({ status: 'authenticating', user: null })
    me.resolve(userA)
    await login
    expect(session.store.getState()).toMatchObject({ status: 'authenticated', user: userA })
    expect(JSON.stringify(session.store.getState())).not.toContain('access-a')
    expect(JSON.stringify(session.store.getState())).not.toContain(tokenPair().refreshToken)
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('cleans up a successful login if its profile cannot be verified', async () => {
    const api = createMockAuthApi()
    api.me.mockRejectedValue(new ApiError('Offline', 'network'))
    const session = createAuthSession({ api, onScopeChange: vi.fn() })
    await expect(session.login(loginCommand)).rejects.toMatchObject({ kind: 'network' })
    expect(session.store.getState().status).toBe('anonymous')
    expect(api.logout).toHaveBeenCalledWith(tokenPair().refreshToken)
  })

  it('does not replace /me permissions with login-response permissions', async () => {
    const api = createMockAuthApi()
    api.login.mockResolvedValue(tokenPair('a', { ...userA, permissions: [] }))
    const session = createAuthSession({ api, onScopeChange: vi.fn() })
    await session.login(loginCommand)
    expect(session.store.getState().user?.permissions).toEqual(userA.permissions)
  })

  it('ends the session if /me returns a different identity', async () => {
    const { api, session } = await signedIn()
    api.me.mockResolvedValue(userB)
    await expect(session.refreshProfile()).rejects.toMatchObject({ status: 401 })
    expect(session.store.getState().status).toBe('anonymous')
  })
})

describe('refresh rotation and request races', () => {
  it('shares one refresh across concurrent 401s and replays with the new access token', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<LoginResponse>()
    api.refresh.mockReturnValue(gate.promise)
    const send = vi.fn(async (token: string) => {
      if (token === 'access-a') throw denied()
      return token
    })
    const requests = [session.authorize(send), session.authorize(send), session.authorize(send)]
    await vi.waitFor(() => expect(api.refresh).toHaveBeenCalledTimes(1))
    gate.resolve(tokenPair('b'))
    expect(await Promise.all(requests)).toEqual(['access-b', 'access-b', 'access-b'])
    expect(api.refresh).toHaveBeenCalledWith(tokenPair().refreshToken)
    expect(send).toHaveBeenCalledTimes(6)
  })

  it('reuses the rotated access token when an old request reports 401 late', async () => {
    const { api, session } = await signedIn()
    const late = deferred<string>()
    const sendLate = vi.fn((token: string) => token === 'access-a' ? late.promise : Promise.resolve(token))
    const request = session.authorize(sendLate)
    await session.authorize(async token => { if (token === 'access-a') throw denied(); return token })
    late.reject(denied())
    expect(await request).toBe('access-b')
    expect(api.refresh).toHaveBeenCalledTimes(1)
  })

  it('waits for an in-flight refresh before sending new protected requests', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<LoginResponse>()
    api.refresh.mockReturnValue(gate.promise)
    const first = session.authorize(async token => { if (token === 'access-a') throw denied(); return token })
    await vi.waitFor(() => expect(api.refresh).toHaveBeenCalledTimes(1))
    const sendSecond = vi.fn(async (token: string) => token)
    const second = session.authorize(sendSecond)
    expect(sendSecond).not.toHaveBeenCalled()
    gate.resolve(tokenPair('b'))
    expect(await first).toBe('access-b')
    expect(await second).toBe('access-b')
    expect(sendSecond).toHaveBeenCalledTimes(1)
  })

  it('preflights a known expired access token with one refresh', async () => {
    const { api, session } = await signedIn()
    vi.useFakeTimers()
    vi.setSystemTime(Date.now() + 901_000)
    api.refresh.mockResolvedValue(tokenPair('b'))
    const send = vi.fn(async (token: string) => token)
    expect(await session.authorize(send)).toBe('access-b')
    expect(send).toHaveBeenCalledTimes(1)
    expect(api.refresh).toHaveBeenCalledTimes(1)
  })

  it.each([403, 429])('does not refresh HTTP %i from a protected resource', async status => {
    const { api, session } = await signedIn()
    const send = vi.fn().mockRejectedValue(new ApiError('Rejected', 'http', status))
    await expect(session.authorize(send)).rejects.toMatchObject({ status })
    expect(api.refresh).not.toHaveBeenCalled()
    expect(session.store.getState().status).toBe('authenticated')
    expect(send).toHaveBeenCalledTimes(1)
  })

  it('limits a protected request to one replay and ends a revoked/inactive session', async () => {
    const { api, session } = await signedIn()
    const send = vi.fn().mockRejectedValue(denied())
    await expect(session.authorize(send)).rejects.toMatchObject({ status: 401 })
    expect(api.refresh).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledTimes(2)
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'revoked' })
  })

  it('never reuses a refresh token after a lost/uncertain refresh response', async () => {
    const { api, session } = await signedIn()
    api.refresh.mockRejectedValue(new ApiError('Lost response', 'timeout'))
    await expect(session.authorize(async () => { throw denied() })).rejects.toMatchObject({ kind: 'timeout' })
    await expect(session.authorize(async () => 'unused')).rejects.toMatchObject({ status: 401 })
    expect(api.refresh).toHaveBeenCalledTimes(1)
    expect(session.store.getState().reason).toBe('refreshFailed')
  })

  it('honors Retry-After on a refresh 429 without spinning or discarding a known unused token', async () => {
    const { api, session } = await signedIn()
    api.refresh.mockRejectedValueOnce(new ApiError('Wait', 'http', 429, {}, 30))
    const send = async (token: string) => { if (token === 'access-a') throw denied(); return token }
    await expect(session.authorize(send)).rejects.toMatchObject({ status: 429 })
    await expect(session.authorize(send)).rejects.toMatchObject({ status: 429 })
    expect(api.refresh).toHaveBeenCalledTimes(1)
    expect(session.store.getState().status).toBe('authenticated')
    vi.useFakeTimers()
    vi.setSystemTime(Date.now() + 31_000)
    expect(await session.authorize(send)).toBe('access-b')
    expect(api.refresh).toHaveBeenCalledTimes(2)
  })

  it('does not let a cancelled waiter cancel the shared refresh for other callers', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<LoginResponse>()
    api.refresh.mockReturnValue(gate.promise)
    const controller = new AbortController()
    const send = async (token: string) => { if (token === 'access-a') throw denied(); return token }
    const first = session.authorize(send, controller.signal).catch(error => error)
    const second = session.authorize(send)
    await vi.waitFor(() => expect(api.refresh).toHaveBeenCalledTimes(1))
    controller.abort()
    gate.resolve(tokenPair('b'))
    expect(await first).toMatchObject({ kind: 'cancelled' })
    expect(await second).toBe('access-b')
    expect(session.store.getState().status).toBe('authenticated')
  })
})

describe('logout, account switches and private cache boundaries', () => {
  it('logs out immediately during refresh and never adopts the late rotation', async () => {
    const { api, session, onScopeChange } = await signedIn()
    const gate = deferred<LoginResponse>()
    api.refresh.mockReturnValue(gate.promise)
    const request = session.authorize(async () => { throw denied() }).catch(error => error)
    await vi.waitFor(() => expect(api.refresh).toHaveBeenCalledTimes(1))
    const logout = session.logout()
    expect(session.store.getState().status).toBe('anonymous')
    expect(onScopeChange).toHaveBeenCalledTimes(2)
    expect(api.logout).toHaveBeenCalledWith(tokenPair().refreshToken)
    gate.resolve(tokenPair('b'))
    await logout
    expect(await request).toMatchObject({ kind: 'cancelled' })
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'signedOut' })
  })

  it('cannot resurrect an abandoned login after logout', async () => {
    const api = createMockAuthApi()
    const gate = deferred<LoginResponse>()
    api.login.mockReturnValue(gate.promise)
    const session = createAuthSession({ api, onScopeChange: vi.fn() })
    const login = session.login(loginCommand).catch(error => error)
    await session.logout()
    gate.resolve(tokenPair())
    expect(await login).toMatchObject({ kind: 'cancelled' })
    expect(api.logout).toHaveBeenCalledWith(tokenPair().refreshToken)
    expect(session.store.getState().status).toBe('anonymous')
  })

  it('discards an old response even if the underlying request ignores AbortSignal', async () => {
    const { api, session } = await signedIn()
    const old = deferred<string>()
    const request = session.authorize(() => old.promise).catch(error => error)
    api.login.mockResolvedValue(tokenPair('c', userB))
    api.me.mockResolvedValue(userB)
    await session.login({ ...loginCommand, email: userB.email })
    old.resolve('private data for user A')
    expect(await request).toMatchObject({ kind: 'cancelled' })
    expect(session.store.getState().user?.id).toBe(userB.id)
  })

  it('keeps the new account when a previous logout fails late', async () => {
    const { api, session } = await signedIn()
    const logoutGate = deferred<void>()
    api.logout.mockReturnValue(logoutGate.promise)
    const logout = session.logout()
    api.login.mockResolvedValue(tokenPair('c', userB))
    api.me.mockResolvedValue(userB)
    await session.login({ ...loginCommand, email: userB.email })
    logoutGate.reject(new ApiError('Network', 'network'))
    await logout
    expect(session.store.getState()).toMatchObject({ status: 'authenticated', user: userB, reason: null })
  })

  it('does not claim remote revocation succeeded when logout is rate limited', async () => {
    const { api, session } = await signedIn()
    api.logout.mockRejectedValue(new ApiError('Wait', 'http', 429, {}, 20))
    await session.logout()
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'logoutFailed', logoutError: { status: 429 } })
  })

  it('updates current grants and rotates the cache scope after /me detects a change', async () => {
    const { api, session, onScopeChange } = await signedIn()
    const oldScope = session.store.getState().scope
    api.me.mockResolvedValue({ ...userA, permissions: [] })
    await session.refreshProfile()
    expect(onScopeChange).toHaveBeenCalledTimes(2)
    expect(session.store.getState().scope).not.toBe(oldScope)
    expect(session.store.getState().user?.permissions).toEqual([])
  })

  it('expires the absolute session without attempting refresh', async () => {
    const { api, session } = await signedIn()
    vi.useFakeTimers()
    vi.setSystemTime(Date.now() + 86_401_000)
    session.expireIfNeeded()
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'expired' })
    expect(api.refresh).not.toHaveBeenCalled()
  })

  it('discards in-flight data authorized under grants that /me has since changed', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<string>()
    const request = session.authorize(() => gate.promise).catch(error => error)
    api.me.mockResolvedValue({ ...userA, permissions: [] })
    await session.refreshProfile()
    gate.resolve('content for the previous permission scope')
    expect(await request).toMatchObject({ kind: 'cancelled' })
    expect(session.store.getState().status).toBe('authenticated')
  })

  it('does not replace a fresh refresh profile with an older /me response or display its cancellation as an error', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<UserProfile>()
    api.me.mockReturnValue(gate.promise)
    const profile = session.refreshProfile().catch(error => error)
    api.refresh.mockResolvedValue(tokenPair('b', { ...userA, permissions: [] }))
    await session.authorize(async token => { if (token === 'access-a') throw denied(); return token })
    gate.resolve(userA)
    expect(await profile).toMatchObject({ kind: 'cancelled' })
    expect(session.store.getState()).toMatchObject({ user: { permissions: [] }, profileError: null })
  })
})

describe('password changes and session invalidation', () => {
  it('can refresh and replay one password request rejected with 401 without waiting on itself', async () => {
    const { api, session } = await signedIn()
    api.changePassword.mockRejectedValueOnce(denied()).mockResolvedValue(undefined)
    await session.changePassword(passwordCommand)
    expect(api.refresh).toHaveBeenCalledTimes(1)
    expect(api.changePassword).toHaveBeenCalledTimes(2)
    expect(api.changePassword.mock.calls[1]?.[0]).toBe('access-b')
    expect(session.store.getState().reason).toBe('passwordChanged')
  })

  it('waits for refresh, changes password with the latest access token and clears local identity', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<LoginResponse>()
    api.refresh.mockReturnValue(gate.promise)
    const pending = session.authorize(async token => { if (token === 'access-a') throw denied(); return token })
    await vi.waitFor(() => expect(api.refresh).toHaveBeenCalledTimes(1))
    const change = session.changePassword(passwordCommand)
    expect(api.changePassword).not.toHaveBeenCalled()
    gate.resolve(tokenPair('b'))
    await pending
    await change
    expect(api.changePassword).toHaveBeenCalledWith('access-b', expect.any(AbortSignal), passwordCommand)
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'passwordChanged' })
  })

  it('does not rotate credentials underneath a password request', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<void>()
    api.changePassword.mockReturnValue(gate.promise)
    const change = session.changePassword(passwordCommand)
    const send = vi.fn(async () => { throw denied() })
    const request = session.authorize(send).catch(error => error)
    expect(api.refresh).not.toHaveBeenCalled()
    gate.resolve(undefined)
    await change
    expect(await request).toMatchObject({ kind: 'cancelled' })
    expect(send).not.toHaveBeenCalled()
    expect(api.refresh).not.toHaveBeenCalled()
  })

  it.each([400, 409, 429])('preserves the session after a known password failure HTTP %i', async status => {
    const { api, session } = await signedIn()
    api.changePassword.mockRejectedValue(new ApiError('Rejected', 'http', status))
    await expect(session.changePassword(passwordCommand)).rejects.toMatchObject({ status })
    expect(session.store.getState().status).toBe('authenticated')
    expect(api.refresh).not.toHaveBeenCalled()
  })

  it('requires login after an uncertain password-change result instead of retrying the command', async () => {
    const { api, session } = await signedIn()
    api.changePassword.mockRejectedValue(new ApiError('Timeout', 'timeout'))
    await expect(session.changePassword(passwordCommand)).rejects.toMatchObject({ kind: 'timeout' })
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'passwordUncertain' })
    expect(api.changePassword).toHaveBeenCalledTimes(1)
  })

  it('does not block or clear a new identity when an old password request finishes late', async () => {
    const { api, session } = await signedIn()
    const gate = deferred<void>()
    api.changePassword.mockReturnValue(gate.promise)
    const oldChange = session.changePassword(passwordCommand).catch(error => error)
    api.login.mockResolvedValue(tokenPair('c', userB))
    api.me.mockResolvedValue(userB)
    await session.login({ ...loginCommand, email: userB.email })
    gate.resolve(undefined)
    expect(await oldChange).toMatchObject({ kind: 'cancelled' })
    expect(session.store.getState()).toMatchObject({ status: 'authenticated', user: userB })
  })
})
