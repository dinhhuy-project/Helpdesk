import { describe, expect, it } from 'vitest'
import { AxiosHeaders } from 'axios'
import { createHttpClient } from '../../../shared/api/http-client'
import { createPublicCommunityApi } from './community-api'

function source(classification?: string) {
  const row = { id: 'example', classification }
  const client = createHttpClient({ baseURL: '/', adapter: async config => ({ config, status: 200, statusText: 'OK', headers: new AxiosHeaders(),
    data: config.url?.includes('pageSize=') ? { items: [row], page: 1, pageSize: 12, totalCount: 1 } : row }) })
  return createPublicCommunityApi(client)
}
describe('public knowledge dataset contract', () => {
  it('posts a stateless assistant question without Bearer, model selection or automatic retry', async () => {
    const controller = new AbortController()
    let calls = 0
    const client = createHttpClient({ baseURL: '/', adapter: async config => {
      calls++
      expect(config.method).toBe('post')
      expect(config.url).toBe('/api/v1/public/assistant')
      expect(JSON.parse(config.data as string)).toEqual({ question: 'doi mk' })
      expect(config.timeout).toBe(125000)
      expect(config.signal).toBe(controller.signal)
      expect(config.headers.has('Authorization')).toBe(false)
      return { config, status: 200, statusText: 'OK', headers: new AxiosHeaders(), data: { status: 'InsufficientInformation', answer: 'Chưa đủ thông tin', sources: [],
        contact: { message: 'Liên hệ hỗ trợ', createTicketUrl: '/login?returnTo=%2Ftickets%2Fnew', knowledgeUrl: '/knowledge' }, method: 'local-extractive-v1' } }
    } })
    const result = await createPublicCommunityApi(client).askAssistant('doi mk', controller.signal)
    expect(result.status).toBe('InsufficientInformation')
    expect(result.sources).toEqual([])
    expect(calls).toBe(1)
  })
  it('encodes retrieval questions, propagates cancellation and sends no authorization', async () => {
    const controller = new AbortController()
    let requested = ''
    const client = createHttpClient({ baseURL: '/', adapter: async config => {
      requested = config.url ?? ''
      expect(config.signal).toBe(controller.signal)
      expect(config.headers.has('Authorization')).toBe(false)
      return { config, status: 200, statusText: 'OK', headers: new AxiosHeaders(), data: { query: 'VPN & OTP', items: [], method: 'lexical-v1', candidateLimitReached: false } }
    } })
    await createPublicCommunityApi(client).retrieve('VPN & OTP', controller.signal)
    expect(requested).toBe('/api/v1/public/knowledge/retrieve?query=VPN%20%26%20OTP&limit=5')
  })
  it('rejects legacy responses without classification instead of presenting them as operational', async () => {
    await expect(source().list('knowledge', { page: 1, search: '' })).rejects.toThrow(/API chưa được nâng cấp/)
  })
  it('never presents a demo result as an operational search result', async () => {
    await expect(source('Demo').list('knowledge', { page: 1, search: '' })).rejects.toThrow(/chưa đúng chế độ/)
    expect((await source('Demo').list('knowledge', { page: 1, search: '', dataset: 'demo' })).items).toHaveLength(1)
  })
  it('checks detail classification even if a stale server ignores the requested dataset', async () => {
    await expect(source('Demo').get('knowledge', 'example')).rejects.toThrow(/chưa đúng chế độ/)
    await expect(source('Operational').get('knowledge', 'example', undefined, 'demo')).rejects.toThrow(/chưa đúng chế độ/)
    expect((await source('Operational').get('knowledge', 'example')).classification).toBe('Operational')
  })
})
