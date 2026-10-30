import type { AxiosAdapter } from 'axios'
import { createAuthApi } from '../../features/auth/api/auth-api'
import { createAuthSession } from '../../features/auth/session/auth-session'
import { createHttpClient } from '../../shared/api/http-client'
import { environment } from '../../shared/config/environment'
import { createQueryClient } from './query-client'
import { createNotificationRealtime } from '../../features/notifications/realtime/notification-realtime'
import type { NotificationConnectionFactory } from '../../features/notifications/realtime/hub-connection'

export function createAppServices(http: { baseURL: string; adapter?: AxiosAdapter } = { baseURL: environment.apiBaseUrl },
  realtimeOptions: { connectionFactory?: NotificationConnectionFactory } = {}) {
  const queryClient = createQueryClient()
  const publicClient = createHttpClient(http)
  const session = createAuthSession({
    api: createAuthApi(publicClient),
    onScopeChange: () => { void queryClient.cancelQueries(); queryClient.clear() },
  })
  const client = createHttpClient({ ...http, authorize: session.authorize })
  const realtime = createNotificationRealtime({ session, queries: queryClient,
    url: http.baseURL === '/' ? '/hubs/notifications' : new URL('/hubs/notifications', http.baseURL).href, ...realtimeOptions })
  return { queryClient, session, client, realtime }
}

export type AppServices = ReturnType<typeof createAppServices>
