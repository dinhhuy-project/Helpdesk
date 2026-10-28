import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getHealth } from '../../features/home/api/health'
import { ApiError } from '../../shared/api/api-error'
import { createQueryClient } from '../providers/query-client'
import { appRoutes } from './routes'

vi.mock('../../features/home/api/health', () => ({ getHealth: vi.fn() }))

const cleanups: (() => void)[] = []
function openPage(path: string) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  cleanups.push(() => { router.dispose(); client.clear() })
  return render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
}

beforeEach(() => { vi.mocked(getHealth).mockResolvedValue(undefined) })
afterEach(() => { for (const cleanup of cleanups.splice(0)) cleanup() })

describe('application routes and API states', () => {
  it('opens the home page and displays a successful liveness request', async () => {
    openPage('/')
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Kết nối hỗ trợ.')
    expect(await screen.findByText('Dịch vụ đang phản hồi')).toBeInTheDocument()
    expect(getHealth).toHaveBeenCalledWith(expect.any(AbortSignal))
  })

  it('shows loading before a request finishes', () => {
    vi.mocked(getHealth).mockImplementation(() => new Promise(() => {}))
    openPage('/')
    expect(screen.getByRole('status')).toHaveTextContent('Đang kiểm tra kết nối')
    expect(screen.queryByText('Dịch vụ đang phản hồi')).not.toBeInTheDocument()
  })

  it('shows a traceable error and allows an explicit retry', async () => {
    vi.mocked(getHealth).mockRejectedValueOnce(new ApiError('Không kết nối được', 'network', undefined, { traceId: 'abc-123' }))
    openPage('/')
    expect(await screen.findByRole('alert')).toHaveTextContent('abc-123')
    await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }))
    expect(await screen.findByText('Dịch vụ đang phản hồi')).toBeInTheDocument()
  })

  it('handles a deep link that does not exist and can navigate home', async () => {
    openPage('/unknown/deep-link')
    expect(screen.getByRole('heading', { name: 'Không tìm thấy trang' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('link', { name: 'Về trang chủ' }))
    expect(await screen.findByText('Dịch vụ đang phản hồi')).toBeInTheDocument()
  })
})
