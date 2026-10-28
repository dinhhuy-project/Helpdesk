import type { AxiosAdapter } from 'axios'
import { createAuthApi } from '../../features/auth/api/auth-api'
import { createAuthSession } from '../../features/auth/session/auth-session'
import { createHttpClient } from '../../shared/api/http-client'
import { environment } from '../../shared/config/environment'
import { createQueryClient } from './query-client'

export function createAppServices(http: { baseURL: string; adapter?: AxiosAdapter } = { baseURL: environment.apiBaseUrl }) {
  const queryClient = createQueryClient()
  const publicClient = createHttpClient(http)
  const session = createAuthSession({
    api: createAuthApi(publicClient),
    onScopeChange: () => { void queryClient.cancelQueries(); queryClient.clear() },
  })
  const client = createHttpClient({ ...http, authorize: session.authorize })
  return { queryClient, session, client }
}

export type AppServices = ReturnType<typeof createAppServices>
