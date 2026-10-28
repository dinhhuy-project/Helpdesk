import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../shared/api/api-error'
import { deferred } from '../../../test/auth-fixtures'
import { useCredentialForm } from './use-credential-form'

afterEach(() => { vi.useRealTimers() })

describe('sensitive command form', () => {
  it('blocks duplicate submissions before React has rerendered', async () => {
    const gate = deferred<void>()
    const command = vi.fn().mockReturnValue(gate.promise)
    const { result } = renderHook(() => useCredentialForm(command))
    let submit: Promise<void> | undefined
    act(() => { submit = result.current.submit('secret'); void result.current.submit('secret') })
    expect(command).toHaveBeenCalledTimes(1)
    expect(result.current.pending).toBe(true)
    await act(async () => { gate.resolve(undefined); await submit })
    expect(result.current.pending).toBe(false)
  })

  it('honors Retry-After and enables a deliberate retry after the deadline', async () => {
    vi.useFakeTimers()
    const command = vi.fn().mockRejectedValueOnce(new ApiError('Wait', 'http', 429, {}, 10)).mockResolvedValue(undefined)
    const { result } = renderHook(() => useCredentialForm(command))
    await act(async () => { await result.current.submit('secret') })
    expect(result.current.remaining).toBe(10)
    expect(result.current.disabled).toBe(true)
    await act(async () => { await result.current.submit('secret') })
    expect(command).toHaveBeenCalledTimes(1)
    await act(async () => { vi.advanceTimersByTime(10_000) })
    expect(result.current.disabled).toBe(false)
    await act(async () => { await result.current.submit('secret') })
    expect(command).toHaveBeenCalledTimes(2)
  })
})
