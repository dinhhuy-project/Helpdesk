import { createContext } from 'react'
import type { NotificationRealtime } from './notification-realtime'

export const RealtimeContext = createContext<NotificationRealtime | null>(null)
