import { QueryClientProvider } from '@tanstack/react-query'
import { useState } from 'react'
import type { PropsWithChildren } from 'react'
import { AuthProvider } from '../../features/auth/providers/auth-provider'
import { createAppServices } from './app-services'
import { RealtimeProvider } from '../../features/notifications/realtime/realtime-provider'

export function AppProviders({ children }: PropsWithChildren) {
  const [services] = useState(createAppServices)
  return <QueryClientProvider client={services.queryClient}>
    <AuthProvider session={services.session} client={services.client}><RealtimeProvider realtime={services.realtime}>{children}</RealtimeProvider></AuthProvider>
  </QueryClientProvider>
}
