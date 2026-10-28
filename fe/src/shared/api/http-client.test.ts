import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { describe, expect, it, vi } from 'vitest'
import { createHttpClient } from './http-client'

function respond(config: InternalAxiosRequestConfig, data: unknown) {
  return { status: 200, statusText: 'OK', headers: new AxiosHeaders(), config, data }
}

describe('typed HTTP transport', () => {
  it('passes query and cancellation signal and returns the response data', async () => {
    const controller = new AbortController()
    const adapter = vi.fn<AxiosAdapter>(async config => {
      expect(config.baseURL).toBe('https://api.example.test')
      expect(config.params).toEqual({ page: 2 })
      expect(config.signal).toBe(controller.signal)
      expect(config.withCredentials).toBe(false)
      return respond(config, { items: [{ id: 'ticket-1' }], page: 2 })
    })
    const client = createHttpClient({ baseURL: 'https://api.example.test', adapter })
    const result = await client.get<{ items: { id: string }[]; page: number }>('/api/v1/tickets', {
      params: { page: 2 }, signal: controller.signal,
    })
    expect(result.items[0]?.id).toBe('ticket-1')
    expect(adapter).toHaveBeenCalledTimes(1)
  })

  it.each(['https://outside.test/api', '//outside.test/api', '/\\outside.test/api', '/\t/outside.test', 'api/v1/tickets'])(
    'rejects off-origin or ambiguous request paths: %s', async path => {
      const adapter = vi.fn<AxiosAdapter>(async config => respond(config, null))
      const client = createHttpClient({ baseURL: '/', adapter })
      await expect(client.get(path)).rejects.toThrow('API path')
      expect(adapter).not.toHaveBeenCalled()
    },
  )

  it('leaves multipart boundary to the browser rather than forcing JSON', async () => {
    const body = new FormData()
    body.append('file', new File(['content'], 'test.txt', { type: 'text/plain' }))
    const adapter = vi.fn<AxiosAdapter>(async config => {
      expect(config.data).toBe(body)
      expect(config.headers.getContentType()).not.toBe('application/json')
      return respond(config, { id: 'attachment' })
    })
    await createHttpClient({ baseURL: '/', adapter }).post('/api/v1/tickets/id/attachments', body)
  })

  it('normalizes HTTP failures without retrying mutations', async () => {
    const adapter = vi.fn<AxiosAdapter>(async config => {
      throw new AxiosError('Conflict', 'ERR_BAD_RESPONSE', config, {}, {
        ...respond(config, { title: 'Version đã thay đổi.', code: 'VersionConflict', traceId: 'abc' }), status: 409,
      })
    })
    const client = createHttpClient({ baseURL: '/', adapter })
    await expect(client.post('/api/v1/tickets/id/assign', { version: 1 })).rejects.toMatchObject({
      status: 409, code: 'VersionConflict', traceId: 'abc',
    })
    expect(adapter).toHaveBeenCalledTimes(1)
  })

  it('does not send an already aborted request', async () => {
    const adapter = vi.fn<AxiosAdapter>(async config => respond(config, null))
    const controller = new AbortController()
    controller.abort()
    await expect(createHttpClient({ baseURL: '/', adapter }).get('/health/live', { signal: controller.signal }))
      .rejects.toMatchObject({ kind: 'cancelled' })
    expect(adapter).not.toHaveBeenCalled()
  })
})
