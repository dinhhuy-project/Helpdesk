import { describe, expect, it, vi } from 'vitest'
import { apiClient } from '../../../shared/api/http-client'
import { getHealth } from './health'

describe('health response contract', () => {
  it('does not interpret an SPA fallback HTML response as a healthy backend', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue('<!doctype html><html>SPA</html>')
    await expect(getHealth()).rejects.toMatchObject({ kind: 'unexpected' })
  })
})
