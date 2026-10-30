import { createStore } from 'zustand/vanilla'
import { ApiError, toApiError } from '../../../shared/api/api-error'
import type { AuthorizeRequest } from '../../../shared/api/http-client'
import type { AuthApi } from '../api/auth-api'
import type { ChangePasswordRequest, LoginRequest, LoginResponse, SessionEndReason, UserProfile } from '../types/auth'

export interface AuthState {
  status: 'anonymous' | 'authenticating' | 'authenticated'
  user: UserProfile | null
  scope: string | null
  sessionExpiresAt: string | null
  reason: SessionEndReason | null
  profilePending: boolean
  profileError: ApiError | null
  logoutError: ApiError | null
}

const anonymous: AuthState = {
  status: 'anonymous', user: null, scope: null, sessionExpiresAt: null, reason: null,
  profilePending: false, profileError: null, logoutError: null,
}

const cancelled = () => new ApiError('Phiên đã thay đổi hoặc yêu cầu đã được hủy.', 'cancelled')
const unauthorized = () => new ApiError('Phiên đăng nhập không hợp lệ hoặc đã hết hạn.', 'http', 401)

export function createAuthSession({ api, onScopeChange }: { api: AuthApi; onScopeChange: () => void }) {
  const store = createStore<AuthState>(() => ({ ...anonymous }))
  // Credentials stay in this closure, outside React props, Query cache, Zustand and browser storage.
  let credentials: LoginResponse | null = null
  let epoch = 0
  let scopeRevision = 0
  let controller = new AbortController()
  let refreshFlight: { epoch: number; promise: Promise<void> } | null = null
  let profileFlight: { epoch: number; promise: Promise<void> } | null = null
  let passwordFlight: { epoch: number; promise: Promise<void> } | null = null
  let logoutFlight: Promise<void> | null = null
  let profileRevision = 0
  let refreshCooldown: { until: number; error: ApiError } | null = null
  let credentialRevision = 0
  const credentialListeners = new Set<() => void>()
  function credentialsChanged() {
    credentialRevision++
    for (const listener of credentialListeners) listener()
  }

  function reset(reason: SessionEndReason | null, status: AuthState['status'] = 'anonymous') {
    epoch++
    controller.abort()
    controller = new AbortController()
    credentials = null
    credentialsChanged()
    refreshCooldown = null
    // Clear private cache before publishing the next identity to subscribers.
    onScopeChange()
    store.setState({ ...anonymous, status, reason }, true)
    return epoch
  }

  function check(currentEpoch: number, signal?: AbortSignal) {
    if (currentEpoch !== epoch || signal?.aborted) throw cancelled()
    if (!credentials) throw unauthorized()
    if (Date.parse(credentials.refreshTokenExpiresAt) <= Date.now()) {
      reset('expired')
      throw unauthorized()
    }
  }

  function currentCredentials(currentEpoch: number, signal?: AbortSignal) {
    check(currentEpoch, signal)
    const current = credentials
    if (!current) throw unauthorized()
    return current
  }

  function publishUser(user: UserProfile) {
    if (!credentials || user.id !== credentials.user.id) { reset('revoked'); throw unauthorized() }
    const previous = store.getState().user
    const changed = previous && (previous.departmentId !== user.departmentId
      || [...previous.roles].sort().join('\0') !== [...user.roles].sort().join('\0')
      || [...previous.permissions].sort().join('\0') !== [...user.permissions].sort().join('\0'))
    if (changed) { scopeRevision++; onScopeChange() }
    store.setState({ user, scope: `${epoch}:${scopeRevision}:${user.id}`,
      sessionExpiresAt: credentials.refreshTokenExpiresAt, profileError: null })
  }

  async function refresh(currentEpoch: number) {
    check(currentEpoch)
    if (refreshFlight?.epoch === currentEpoch) return refreshFlight.promise
    if (refreshCooldown && Date.now() < refreshCooldown.until) {
      throw new ApiError(refreshCooldown.error.message, 'http', 429, refreshCooldown.error.problem,
        Math.ceil((refreshCooldown.until - Date.now()) / 1000))
    }
    const token = currentCredentials(currentEpoch).refreshToken
    const promise = (async () => {
      try {
        const response = await api.refresh(token)
        check(currentEpoch)
        if (response.user.id !== currentCredentials(currentEpoch).user.id || response.refreshToken === token) throw unauthorized()
        credentials = response
        refreshCooldown = null
        profileRevision++ // A profile response using the previous token cannot overwrite this one.
        if (store.getState().status === 'authenticated') publishUser(response.user)
        credentialsChanged()
      } catch (error) {
        const failure = toApiError(error)
        if (currentEpoch === epoch) {
          if (failure.status === 429) {
            refreshCooldown = { until: Date.now() + (failure.retryAfterSeconds ?? 60) * 1000, error: failure }
          } else {
            // A lost response may already have rotated the credential: never send the old token again.
            reset(failure.status === 401 ? 'revoked' : 'refreshFailed')
          }
        }
        throw failure
      } finally {
        if (refreshFlight?.epoch === currentEpoch) refreshFlight = null
      }
    })()
    refreshFlight = { epoch: currentEpoch, promise }
    return promise
  }

  async function authorize<T>(send: (token: string, signal: AbortSignal) => Promise<T>, signal?: AbortSignal,
    changingPassword = false): Promise<T> {
    const currentEpoch = epoch
    check(currentEpoch, signal)
    if (!changingPassword && passwordFlight?.epoch === currentEpoch) await passwordFlight.promise.catch(() => {})
    check(currentEpoch, signal)
    if (refreshFlight?.epoch === currentEpoch) await refreshFlight.promise
    check(currentEpoch, signal)
    if (Date.parse(currentCredentials(currentEpoch).expiresAt) <= Date.now() + 10_000) await refresh(currentEpoch)
    check(currentEpoch, signal)
    const token = currentCredentials(currentEpoch).accessToken
    const sessionSignal = controller.signal
    const run = async (accessToken: string) => {
      check(currentEpoch, signal)
      const sentRevision = scopeRevision
      const result = await send(accessToken, sessionSignal)
      check(currentEpoch, signal)
      if (sentRevision !== scopeRevision) throw cancelled()
      return result
    }
    try {
      return await run(token)
    } catch (error) {
      check(currentEpoch, signal)
      const failure = toApiError(error)
      if (failure.status !== 401) throw failure
      if (!changingPassword && passwordFlight?.epoch === currentEpoch) await passwordFlight.promise.catch(() => {})
      check(currentEpoch, signal)
      // A late 401 for an old access token reuses the already rotated pair.
      if (currentCredentials(currentEpoch).accessToken === token) await refresh(currentEpoch)
      check(currentEpoch, signal)
      try {
        return await run(currentCredentials(currentEpoch).accessToken)
      } catch (retryError) {
        check(currentEpoch, signal)
        const retryFailure = toApiError(retryError)
        if (currentEpoch === epoch && retryFailure.status === 401) reset('revoked')
        throw retryFailure
      }
    }
  }

  async function refreshProfile(options: { background?: boolean } = {}) {
    const currentEpoch = epoch
    check(currentEpoch)
    const state = store.getState()
    // Background checks preserve verified forms. A failed/unverified profile always blocks content.
    if (!options.background || state.profileError || !state.user) {
      store.setState({ profilePending: true, profileError: null })
    }
    if (profileFlight?.epoch === currentEpoch) return profileFlight.promise
    const promise = (async () => {
      try {
        let revision = 0
        const user = await authorize((token, signal) => {
          revision = ++profileRevision
          return api.me(token, signal)
        })
        check(currentEpoch)
        if (revision === profileRevision) publishUser(user)
      } catch (error) {
        const failure = toApiError(error)
        if (currentEpoch === epoch && failure.kind !== 'cancelled') store.setState({ profileError: failure })
        throw failure
      } finally {
        if (profileFlight?.epoch === currentEpoch) profileFlight = null
        if (currentEpoch === epoch) store.setState({ profilePending: false })
      }
    })()
    profileFlight = { epoch: currentEpoch, promise }
    return promise
  }

  async function login(command: LoginRequest) {
    const currentEpoch = reset(null, 'authenticating')
    let received: LoginResponse | null = null
    try {
      received = await api.login(command)
      if (currentEpoch !== epoch) throw cancelled()
      credentials = received
      credentialsChanged()
      await refreshProfile()
      check(currentEpoch)
      if (!store.getState().user) throw unauthorized()
      store.setState({ status: 'authenticated' })
    } catch (error) {
      if (currentEpoch === epoch) reset(null)
      // If login completed after logout/navigation, do not leave its known credential active.
      if (received) {
        try { await api.logout(received.refreshToken) } catch { /* Local credentials stay cleared. */ }
      }
      throw toApiError(error)
    }
  }

  async function logout() {
    if (logoutFlight && !credentials && store.getState().status === 'anonymous') return logoutFlight
    const token = credentials?.refreshToken
    const endedEpoch = reset('signedOut')
    if (!token) return
    const promise = (async () => {
      try {
        // Logout accepts an ancestor token, so it also revokes an in-flight rotation's descendants.
        await api.logout(token)
      } catch (error) {
        if (epoch === endedEpoch) store.setState({ reason: 'logoutFailed', logoutError: toApiError(error) })
      } finally { if (epoch === endedEpoch) logoutFlight = null }
    })()
    logoutFlight = promise
    return promise
  }

  async function changePassword(command: ChangePasswordRequest) {
    const currentEpoch = epoch
    check(currentEpoch)
    if (passwordFlight?.epoch === currentEpoch) return passwordFlight.promise
    const promise: Promise<void> = (async () => {
      try {
        await authorize((token, signal) => api.changePassword(token, signal, command), undefined, true)
        check(currentEpoch)
        reset('passwordChanged')
      } catch (error) {
        const failure = toApiError(error)
        if (epoch === currentEpoch && (['network', 'timeout'].includes(failure.kind) || (failure.status ?? 0) >= 500)) {
          reset('passwordUncertain')
        }
        throw failure
      } finally { if (passwordFlight?.epoch === currentEpoch) passwordFlight = null }
    })()
    passwordFlight = { epoch: currentEpoch, promise }
    return promise
  }

  function expireIfNeeded() {
    if (credentials && Date.parse(credentials.refreshTokenExpiresAt) <= Date.now()) reset('expired')
  }

  const authorizeRequest: AuthorizeRequest = (send, signal) => authorize(send, signal)
  // Only discard the known current local session; this sends no administrative revoke request.
  function endRevokedSession(expectedScope: string) {
    if (store.getState().scope === expectedScope) reset('revoked')
  }
  // The hub receives only an access token, through the same single-flight refresh path as REST.
  // No credential is published to Zustand, React, Query cache or browser storage.
  async function realtimeCredential(expectedScope: string) {
    if (store.getState().scope !== expectedScope) throw cancelled()
    return authorize(async token => {
      if (store.getState().scope !== expectedScope) throw cancelled()
      return { token, expiresAt: currentCredentials(epoch).expiresAt, revision: credentialRevision }
    })
  }
  function subscribeCredentials(listener: () => void) {
    credentialListeners.add(listener)
    return () => { credentialListeners.delete(listener) }
  }
  return { store, login, logout, refreshProfile, changePassword, expireIfNeeded, endRevokedSession, authorize: authorizeRequest,
    realtimeCredential, subscribeCredentials, credentialRevision: () => credentialRevision }
}

export type AuthSession = ReturnType<typeof createAuthSession>
