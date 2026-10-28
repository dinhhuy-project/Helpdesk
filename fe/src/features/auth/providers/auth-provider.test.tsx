import { act, render } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createHttpClient } from '../../../shared/api/http-client'
import { createMockAuthApi, tokenPair, userA } from '../../../test/auth-fixtures'
import { createAuthSession } from '../session/auth-session'
import { AuthProvider } from './auth-provider'

afterEach(() => { vi.useRealTimers() })

describe('provider session lifetime', () => {
  it('ends an absolute-expiry session while the browser is idle without sending refresh', async () => {
    vi.useFakeTimers()
    const api = createMockAuthApi()
    api.login.mockResolvedValue({ ...tokenPair(), refreshTokenExpiresAt: new Date(Date.now() + 1000).toISOString() })
    const session = createAuthSession({ api, onScopeChange: vi.fn() })
    await session.login({ email: userA.email, password: 'test-only-password' })
    const client = createHttpClient({ baseURL: '/', authorize: session.authorize })
    const view = render(<AuthProvider session={session} client={client}><p>Session</p></AuthProvider>)
    await act(async () => { vi.advanceTimersByTime(1001) })
    expect(session.store.getState()).toMatchObject({ status: 'anonymous', reason: 'expired' })
    expect(api.refresh).not.toHaveBeenCalled()
    view.unmount()
  })
})
