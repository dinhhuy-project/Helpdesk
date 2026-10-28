export interface UserProfile {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  departmentId: string | null
  roles: string[]
  permissions: string[]
}

export interface LoginRequest { email: string; password: string }
export interface ChangePasswordRequest { currentPassword: string; newPassword: string }
export interface LoginResponse {
  accessToken: string
  tokenType: string
  expiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  user: UserProfile
}

export type SessionEndReason = 'signedOut' | 'expired' | 'revoked' | 'refreshFailed'
  | 'passwordChanged' | 'passwordUncertain' | 'logoutFailed'
