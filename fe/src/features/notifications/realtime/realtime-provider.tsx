import { useEffect } from 'react'
import type { PropsWithChildren } from 'react'
import { RealtimeContext } from './realtime-context'
import type { NotificationRealtime } from './notification-realtime'

export function RealtimeProvider({ realtime, children }: PropsWithChildren<{ realtime: NotificationRealtime }>) {
  useEffect(() => {
    const detach = realtime.attach()
    const resume = () => { if (document.visibilityState !== 'hidden') realtime.resume() }
    window.addEventListener('focus', resume)
    window.addEventListener('online', resume)
    document.addEventListener('visibilitychange', resume)
    return () => {
      window.removeEventListener('focus', resume)
      window.removeEventListener('online', resume)
      document.removeEventListener('visibilitychange', resume)
      detach()
    }
  }, [realtime])
  return <RealtimeContext.Provider value={realtime}>{children}</RealtimeContext.Provider>
}
