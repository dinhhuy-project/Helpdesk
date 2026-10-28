import { vi } from 'vitest'
import type { AuthApi } from '../features/auth/api/auth-api'
import type { LoginResponse, UserProfile } from '../features/auth/types/auth'

export const userA: UserProfile = {
  id: '00000000-0000-0000-0000-000000000001', email: 'requester@example.test', displayName: 'Nguyễn An',
  avatarUrl: null, departmentId: null, roles: ['Requester'], permissions: ['ticket.create', 'ticket.read.own'],
}
export const userB: UserProfile = { ...userA, id: '00000000-0000-0000-0000-000000000002', email: 'agent@example.test', displayName: 'Trần Bình' }

export function tokenPair(label = 'a', user: UserProfile = userA): LoginResponse {
  return { accessToken: `access-${label}`, tokenType: 'Bearer', refreshToken: label.padEnd(86, '_'),
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(), user }
}

export function createMockAuthApi() {
  return {
    login: vi.fn<AuthApi['login']>().mockResolvedValue(tokenPair()),
    refresh: vi.fn<AuthApi['refresh']>().mockResolvedValue(tokenPair('b')),
    logout: vi.fn<AuthApi['logout']>().mockResolvedValue(undefined),
    me: vi.fn<AuthApi['me']>().mockResolvedValue(userA),
    changePassword: vi.fn<AuthApi['changePassword']>().mockResolvedValue(undefined),
  }
}

export function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((accept, decline) => { resolve = accept; reject = decline })
  return { promise, resolve, reject }
}
