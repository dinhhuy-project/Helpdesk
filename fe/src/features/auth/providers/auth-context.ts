import { createContext } from 'react'
import type { HttpClient } from '../../../shared/api/http-client'
import type { AuthSession } from '../session/auth-session'

export const AuthContext = createContext<{ session: AuthSession; client: HttpClient } | null>(null)
