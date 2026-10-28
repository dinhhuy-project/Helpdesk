import { useContext } from 'react'
import { useStore } from 'zustand'
import { AuthContext } from '../providers/auth-context'

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('AuthProvider is required.')
  const state = useStore(context.session.store)
  return { ...context, ...state }
}
