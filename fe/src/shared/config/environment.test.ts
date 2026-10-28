import { describe, expect, it } from 'vitest'
import { readEnvironment } from './environment'

describe('public environment', () => {
  it('uses same-origin transport when no override is provided', () => {
    expect(readEnvironment({}).apiBaseUrl).toBe('/')
  })
  it('accepts a backend origin and normalizes its trailing slash', () => {
    expect(readEnvironment({ VITE_API_BASE_URL: ' https://helpdesk.example:8443/ ' }).apiBaseUrl)
      .toBe('https://helpdesk.example:8443')
  })
  it.each(['//outside.example', 'javascript:alert(1)', 'https://user:password@example.test',
    'https://example.test/api/v1', 'https://example.test?token=secret', 'https://example.test/#fragment', 'not-a-url'])(
    'rejects ambiguous or credential-bearing configuration: %s', value => {
      expect(() => readEnvironment({ VITE_API_BASE_URL: value })).toThrow('VITE_API_BASE_URL')
    },
  )
})
