import { QueryClient } from '@tanstack/react-query'
import { ApiError } from '../../shared/api/api-error'

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: true,
        refetchOnReconnect: true,
        retry: (failureCount, error) => failureCount < 1 && error instanceof ApiError
          && (['network', 'timeout'].includes(error.kind)
            || (error.kind === 'http' && [502, 503, 504].includes(error.status ?? 0))),
      },
      mutations: { retry: false },
    },
  })
}
