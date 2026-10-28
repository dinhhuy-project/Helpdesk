import { describe, expect, it } from 'vitest'
import { tokenPair, userA } from '../../../test/auth-fixtures'
import { parseLoginResponse, parseUserProfile } from './auth-api'

describe('auth response contract', () => {
  it('accepts the backend shape and strips unexpected credential-bearing profile fields', () => {
    expect(parseUserProfile({ ...userA, passwordHash: 'must-not-be-retained' })).toEqual(userA)
    const response = tokenPair()
    expect(parseLoginResponse(response)).toEqual(response)
  })
  it.each([
    { ...tokenPair(), refreshToken: 'short' }, { ...tokenPair(), tokenType: 'Basic' },
    { ...tokenPair(), user: { ...userA, permissions: null } }, { ...tokenPair(), expiresAt: 'invalid' },
    '<html>login proxy fallback</html>',
  ])('rejects malformed credentials without exposing the response body', value => {
    expect(() => parseLoginResponse(value)).toThrow('Phản hồi xác thực không hợp lệ')
  })
})
