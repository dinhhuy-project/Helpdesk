import { AxiosError, AxiosHeaders, CanceledError } from 'axios'
import { describe, expect, it } from 'vitest'
import { ApiError, getFieldError, parseProblemDetails, toApiError } from './api-error'

function httpError(status: number, data: unknown, headers = new AxiosHeaders()) {
  const config = { headers: new AxiosHeaders(), data: 'private-request-body' }
  return new AxiosError('raw transport message', 'ERR_BAD_RESPONSE', config, {}, {
    status, statusText: '', data, headers, config,
  })
}

describe('API error normalization', () => {
  it('keeps validation paths, business code and traceId, without the Axios request', () => {
    const error = toApiError(httpError(400, {
      title: 'Dữ liệu không hợp lệ.', code: 'InvalidInput', traceId: 'trace-123',
      errors: { DisplayName: ['Tên bắt buộc.'], '$.teamId': ['Nhóm không hợp lệ.'] },
    }))
    expect(error.status).toBe(400)
    expect(error.code).toBe('InvalidInput')
    expect(error.traceId).toBe('trace-123')
    expect(getFieldError(error, 'displayName')).toBe('Tên bắt buộc.')
    expect(getFieldError(error, 'teamId')).toBe('Nhóm không hợp lệ.')
    expect(getFieldError(error, 'password')).toBeUndefined()
    expect(JSON.stringify(error)).not.toContain('private-request-body')
    expect(error).not.toHaveProperty('config')
  })

  it('does not display arbitrary proxy HTML or unknown exception messages', () => {
    expect(toApiError(httpError(502, '<html>private proxy trace</html>')).message).not.toContain('private')
    expect(toApiError(new Error('password=secret')).message).not.toContain('secret')
  })

  it('accepts only the documented shape from untrusted JSON', () => {
    expect(parseProblemDetails({ title: 42, errors: { Name: ['Invalid'], Bad: [42] }, exception: 'secret' }))
      .toEqual({ errors: { Name: ['Invalid'] } })
    expect(parseProblemDetails(null)).toEqual({})
  })

  it('uses the HTTP status rather than a conflicting JSON status', () => {
    expect(toApiError(httpError(403, { status: 200 })).status).toBe(403)
  })

  it('preserves rate-limit delay for the UI', () => {
    const error = toApiError(httpError(429, {}, new AxiosHeaders({ 'retry-after': '12' })))
    expect(error.retryAfterSeconds).toBe(12)
  })

  it('separates cancellation, timeout and offline errors', () => {
    expect(toApiError(new CanceledError()).kind).toBe('cancelled')
    expect(toApiError(new AxiosError('raw', 'ECONNABORTED')).kind).toBe('timeout')
    expect(toApiError(new AxiosError('raw', 'ERR_NETWORK')).kind).toBe('network')
  })

  it('does not wrap an already normalized error again', () => {
    const error = new ApiError('Conflict', 'http', 409)
    expect(toApiError(error)).toBe(error)
  })
})
