import { useEffect } from 'react'
import type { PropsWithChildren } from 'react'
import { useStore } from 'zustand'
import type { HttpClient } from '../../../shared/api/http-client'
import type { AuthSession } from '../session/auth-session'
import { AuthContext } from './auth-context'

export function AuthProvider({ session, client, children }: PropsWithChildren<{ session: AuthSession; client: HttpClient }>) {
  const status = useStore(session.store, state => state.status)
  const expiresAt = useStore(session.store, state => state.sessionExpiresAt)

  useEffect(() => {
    if (status !== 'authenticated' || !expiresAt) return
    const expiry = Date.parse(expiresAt)
    let timeout: ReturnType<typeof setTimeout>
    function checkExpiry() {
      const delay = expiry - Date.now()
      if (delay <= 0) session.expireIfNeeded()
      else timeout = setTimeout(checkExpiry, Math.min(delay, 2_147_000_000))
    }
    const checkProfile = () => {
      session.expireIfNeeded()
      if (session.store.getState().status === 'authenticated' && document.visibilityState !== 'hidden') {
        void session.refreshProfile({ background: true }).catch(() => {})
      }
    }
    checkExpiry()
    window.addEventListener('focus', checkProfile)
    window.addEventListener('online', checkProfile)
    document.addEventListener('visibilitychange', checkProfile)
    return () => {
      clearTimeout(timeout)
      window.removeEventListener('focus', checkProfile)
      window.removeEventListener('online', checkProfile)
      document.removeEventListener('visibilitychange', checkProfile)
    }
  }, [session, status, expiresAt])

  return <AuthContext.Provider value={{ session, client }}>{children}</AuthContext.Provider>
}
