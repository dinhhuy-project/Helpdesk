import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../shared/api/api-error'
import { createQueryClient } from './query-client'

describe('query retry policy', () => {
  it.each([401, 403, 404, 409, 429])('does not automatically retry HTTP %i', async status => {
    const client = createQueryClient()
    const queryFn = vi.fn().mockRejectedValue(new ApiError('Failure', 'http', status))
    await expect(client.fetchQuery({ queryKey: ['test', status], queryFn, retryDelay: 0 })).rejects.toBeInstanceOf(ApiError)
    expect(queryFn).toHaveBeenCalledTimes(1)
    client.clear()
  })

  it('retries a transient read failure once, then gives up', async () => {
    const client = createQueryClient()
    const queryFn = vi.fn().mockRejectedValue(new ApiError('Offline', 'network'))
    await expect(client.fetchQuery({ queryKey: ['test'], queryFn, retryDelay: 0 })).rejects.toBeInstanceOf(ApiError)
    expect(queryFn).toHaveBeenCalledTimes(2)
    client.clear()
  })

  it('never automatically replays a mutation after an uncertain network result', async () => {
    const client = createQueryClient()
    const mutationFn = vi.fn().mockRejectedValue(new ApiError('Timeout', 'timeout'))
    const mutation = client.getMutationCache().build(client, { mutationFn })
    await expect(mutation.execute(undefined)).rejects.toBeInstanceOf(ApiError)
    expect(mutationFn).toHaveBeenCalledTimes(1)
    client.clear()
  })
})
