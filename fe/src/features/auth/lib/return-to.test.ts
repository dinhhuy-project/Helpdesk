import { describe, expect, it } from 'vitest'
import { safeReturnTo } from './return-to'

describe('post-login navigation', () => {
  it('preserves an internal path and its query/hash', () => {
    expect(safeReturnTo('/account?tab=profile#name')).toBe('/account?tab=profile#name')
  })
  it.each(['https://outside.test', '//outside.test', '/\\outside.test', '/\t/outside.test', '/login',
    '/LOGIN/', '/lo%67in', '/%', null])('rejects external, invalid and looping destinations: %s', value => {
    expect(safeReturnTo(value)).toBe('/account')
  })
})
