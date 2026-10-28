import { ApiError } from '../../../shared/api/api-error'
import type { HttpClient } from '../../../shared/api/http-client'
import type { ChangePasswordRequest, LoginRequest, LoginResponse, UserProfile } from '../types/auth'

function invalidResponse(): never {
  throw new ApiError('Phản hồi xác thực không hợp lệ. Vui lòng đăng nhập lại.', 'unexpected')
}
function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
function stringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(item => typeof item === 'string')
}

export function parseUserProfile(value: unknown): UserProfile {
  if (!record(value) || typeof value.id !== 'string' || !value.id
    || typeof value.email !== 'string' || typeof value.displayName !== 'string'
    || !(value.avatarUrl === null || typeof value.avatarUrl === 'string')
    || !(value.departmentId === null || typeof value.departmentId === 'string')
    || !stringArray(value.roles) || !stringArray(value.permissions)) invalidResponse()
  return { id: value.id, email: value.email, displayName: value.displayName,
    avatarUrl: value.avatarUrl, departmentId: value.departmentId, roles: value.roles, permissions: value.permissions }
}

export function parseLoginResponse(value: unknown): LoginResponse {
  if (!record(value) || typeof value.accessToken !== 'string' || !value.accessToken
    || value.tokenType !== 'Bearer' || typeof value.refreshToken !== 'string'
    || !/^[A-Za-z0-9_-]{86}$/.test(value.refreshToken)
    || typeof value.expiresAt !== 'string' || !Number.isFinite(Date.parse(value.expiresAt))
    || typeof value.refreshTokenExpiresAt !== 'string' || !Number.isFinite(Date.parse(value.refreshTokenExpiresAt))) invalidResponse()
  return { accessToken: value.accessToken, tokenType: value.tokenType, expiresAt: value.expiresAt,
    refreshToken: value.refreshToken, refreshTokenExpiresAt: value.refreshTokenExpiresAt,
    user: parseUserProfile(value.user) }
}

/** Always use a bare transport: these calls must never trigger an auth refresh recursively. */
export function createAuthApi(client: HttpClient) {
  return {
    login: async (command: LoginRequest) => parseLoginResponse(await client.post<unknown>('/api/v1/auth/login', command)),
    refresh: async (refreshToken: string) => parseLoginResponse(await client.post<unknown>('/api/v1/auth/refresh', { refreshToken })),
    logout: (refreshToken: string) => client.post<void>('/api/v1/auth/logout', { refreshToken }),
    me: async (accessToken: string, signal: AbortSignal) => parseUserProfile(await client.get<unknown>('/api/v1/users/me', {
      headers: { Authorization: `Bearer ${accessToken}` }, signal,
    })),
    changePassword: (accessToken: string, signal: AbortSignal, command: ChangePasswordRequest) =>
      client.post<void>('/api/v1/users/me/password', command, { headers: { Authorization: `Bearer ${accessToken}` }, signal }),
  }
}

export type AuthApi = ReturnType<typeof createAuthApi>
