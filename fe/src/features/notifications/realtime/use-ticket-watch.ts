import { useContext, useEffect } from 'react'
import { useAuth } from '../../auth/hooks/use-auth'
import { RealtimeContext } from './realtime-context'

export function useTicketWatch(id: string, enabled: boolean) {
  const realtime = useContext(RealtimeContext)
  const { scope } = useAuth()
  useEffect(() => {
    if (enabled && scope) return realtime?.watchTicket(id)
  }, [realtime, scope, id, enabled])
}
