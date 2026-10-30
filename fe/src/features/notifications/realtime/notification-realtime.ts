import { createStore } from 'zustand/vanilla'
import type { QueryClient } from '@tanstack/react-query'
import type { AuthSession } from '../../auth/session/auth-session'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import { notificationKeys } from '../hooks/use-notifications'
import { notificationConnectionFactory } from './hub-connection'
import type { NotificationConnection, NotificationConnectionFactory } from './hub-connection'
import { refreshRealtimeInbox, refreshRealtimeTicket } from './realtime-cache'

export type RealtimeStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'offline'
interface ConnectionRun {
  connection: NotificationConnection; scope: string; credentialRevision: number; live: boolean
  applied: Set<string>; watchFlight?: Promise<void>; forceWatch: boolean; stopFlight?: Promise<void>
  handlers: Record<string, (...args: unknown[]) => void>
}
export function createNotificationRealtime({ session, queries, url, connectionFactory = notificationConnectionFactory() }: {
  session: AuthSession; queries: QueryClient; url: string; connectionFactory?: NotificationConnectionFactory
}) {
  const store = createStore<{ status: RealtimeStatus }>(() => ({ status: 'idle' }))
  const wanted = new Map<string, { scope: string; owners: Set<symbol> }>()
  let owners = 0, run: ConnectionRun | null = null, queue = Promise.resolve()
  let unsubscribeStore: (() => void) | undefined, unsubscribeCredentials: (() => void) | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined, renewTimer: ReturnType<typeof setTimeout> | undefined
  let healthTimer: ReturnType<typeof setTimeout> | undefined, batchTimer: ReturnType<typeof setTimeout> | undefined
  let retryAttempt = 0, watchRevision = 0, inboxPending = false, flushing = false
  const ticketPending = new Set<string>(), notificationPending = new Set<string>()
  let signature = ''
  function readyScope() {
    const state = session.store.getState()
    return owners && state.status === 'authenticated' && !state.profilePending && !state.profileError ? state.scope : null
  }
  function current(candidate: ConnectionRun) {
    return candidate.live && run === candidate && readyScope() === candidate.scope && session.credentialRevision() === candidate.credentialRevision
  }
  function status(value: RealtimeStatus) { if (store.getState().status !== value) store.setState({ status: value }) }
  function clearTimers() { clearTimeout(retryTimer); clearTimeout(renewTimer); clearTimeout(healthTimer); retryTimer = renewTimer = healthTimer = undefined }
  function clearBatch() {
    clearTimeout(batchTimer); batchTimer = undefined; inboxPending = false; ticketPending.clear(); notificationPending.clear()
  }
  async function stop(candidate: ConnectionRun) {
    candidate.live = false
    for (const [event, handler] of Object.entries(candidate.handlers)) candidate.connection.off(event, handler)
    candidate.stopFlight ??= candidate.connection.stop().catch(() => {})
    await candidate.stopFlight
    if (run === candidate) run = null
  }
  function retryLater() {
    if (!readyScope() || retryTimer) return
    const delay = [2_000, 5_000, 10_000, 30_000][Math.min(retryAttempt++, 3)]!
    retryTimer = setTimeout(() => { retryTimer = undefined; reconcile() }, delay)
  }
  function reconcile() {
    queue = queue.then(connect).catch(() => { if (readyScope()) { status('offline'); retryLater() } })
  }
  function observe() {
    const scope = readyScope()
    const next = scope ? `${scope}:${session.credentialRevision()}` : ''
    if (signature === next) return
    signature = next; clearTimers(); clearBatch()
    for (const [id, entry] of wanted) if (entry.scope !== scope) wanted.delete(id)
    // Stop immediately even during start/reconnect; a queued start cannot outlive logout/rotation.
    if (run) void stop(run)
    if (!scope) status('idle')
    reconcile()
  }
  async function connect() {
    const scope = readyScope()
    if (run && current(run)) return
    if (run) await stop(run)
    if (!scope || scope !== readyScope()) return
    let credential: Awaited<ReturnType<AuthSession['realtimeCredential']>>
    try { credential = await session.realtimeCredential(scope) }
    catch { if (scope === readyScope()) { status('offline'); retryLater() }; return }
    if (scope !== readyScope()) return
    // Getting the credential may itself rotate it. Build against the revision that actually returned.
    const candidate: ConnectionRun = { connection: connectionFactory(url, async () => {
      const value = await session.realtimeCredential(scope)
      if (!current(candidate) || value.revision !== candidate.credentialRevision) throw new ApiError('Kết nối đã thay đổi.', 'cancelled')
      return value.token
    }), scope, credentialRevision: credential.revision, live: true, applied: new Set(), forceWatch: false, handlers: {} }
    const connection = candidate.connection
    run = candidate
    candidate.handlers = {
      NotificationsChanged: () => { if (current(candidate)) { inboxPending = true; for (const id of wanted.keys()) ticketPending.add(id); scheduleBatch(candidate) } },
      NotificationChanged: value => {
        const id = eventId(value)
        if (current(candidate) && id) { inboxPending = true; notificationPending.add(id); scheduleBatch(candidate) }
      },
      TicketChanged: value => {
        const id = eventId(value)
        if (current(candidate) && id && wanted.get(id)?.scope === scope) { ticketPending.add(id); scheduleBatch(candidate) }
      },
    }
    for (const [event, handler] of Object.entries(candidate.handlers)) connection.on(event, handler)
    connection.onreconnecting(() => { if (current(candidate)) { candidate.applied.clear(); status('reconnecting') } })
    connection.onreconnected(() => { if (current(candidate)) { candidate.applied.clear(); established(candidate) } })
    connection.onclose(() => {
      if (!current(candidate)) return
      candidate.live = false; clearTimers(); status('offline'); retryLater()
    })
    status('connecting')
    try {
      await connection.start()
      if (!current(candidate)) { await stop(candidate); return }
      established(candidate)
    } catch {
      if (!current(candidate)) { await stop(candidate); return }
      await stop(candidate)
      if (scope !== readyScope()) return
      status('offline')
      // A failed negotiation may be an expired/revoked JWT. REST owns 401/rotation/inactive handling.
      await session.refreshProfile({ background: true }).catch(() => {})
      retryLater()
    }
  }
  function established(candidate: ConnectionRun) {
    status('connected'); retryAttempt = 0
    void session.refreshProfile({ background: true }).then(() => {
      if (!current(candidate)) return
      inboxPending = true
      for (const [id, entry] of wanted) if (entry.scope === candidate.scope) ticketPending.add(id)
      scheduleBatch(candidate); syncWatches(candidate, true)
    }).catch(() => {})
    // Renew through auth before JWT expiry, including when no REST request is being made.
    void session.realtimeCredential(candidate.scope).then(value => {
      if (!current(candidate)) return
      clearTimeout(renewTimer)
      renewTimer = setTimeout(() => { void session.realtimeCredential(candidate.scope).catch(() => {}) },
        Math.min(2_147_000_000, Math.max(50, Date.parse(value.expiresAt) - Date.now() - 9_000)))
    }).catch(() => {})
    health(candidate)
  }
  function health(candidate: ConnectionRun) {
    clearTimeout(healthTimer)
    healthTimer = setTimeout(async () => {
      if (!current(candidate)) return
      try { await candidate.connection.invoke('Ping'); syncWatches(candidate, true) }
      catch { if (current(candidate)) { void session.refreshProfile({ background: true }).catch(() => {}); resume() } }
      if (current(candidate)) health(candidate)
    }, 30_000)
  }
  function scheduleBatch(candidate: ConnectionRun) {
    if (batchTimer || flushing || !current(candidate)) return
    batchTimer = setTimeout(() => { batchTimer = undefined; void flush(candidate).catch(() => {}) }, 150)
  }
  async function flush(candidate: ConnectionRun) {
    if (flushing || !current(candidate)) return
    flushing = true
    const inbox = inboxPending, tickets = [...ticketPending], notifications = [...notificationPending]
    inboxPending = false; ticketPending.clear(); notificationPending.clear()
    const valid = () => current(candidate)
    try {
      if (inbox) await refreshRealtimeInbox(queries, candidate.scope, valid)
      if (!valid()) return
      await Promise.all(notifications.map(id => queries.invalidateQueries({ queryKey: notificationKeys.detail(candidate.scope, id) })))
      for (const id of tickets) if (wanted.get(id)?.scope === candidate.scope) await refreshRealtimeTicket(queries, candidate.scope, id, valid)
    } finally {
      flushing = false
      if (valid() && (inboxPending || ticketPending.size || notificationPending.size)) scheduleBatch(candidate)
      else if (run && current(run) && (inboxPending || ticketPending.size || notificationPending.size)) scheduleBatch(run)
    }
  }
  function syncWatches(candidate: ConnectionRun, force = false) {
    candidate.forceWatch ||= force
    if (candidate.watchFlight || !current(candidate) || store.getState().status !== 'connected') return
    candidate.watchFlight = (async () => {
      let revision: number
      do {
        revision = watchRevision
        const renew = candidate.forceWatch; candidate.forceWatch = false
        for (const id of candidate.applied) if (!wanted.has(id)) {
          candidate.applied.delete(id)
          try { await candidate.connection.invoke('UnwatchTicket', id) } catch { /* A reconnect already discards server groups. */ }
          if (!current(candidate)) return
        }
        for (const [id, entry] of wanted) {
          if (!current(candidate)) return
          if (entry.scope !== candidate.scope || (!renew && candidate.applied.has(id))) continue
          try { await candidate.connection.invoke('WatchTicket', id); if (current(candidate)) candidate.applied.add(id) }
          catch { if (current(candidate)) { candidate.applied.delete(id); ticketPending.add(id); scheduleBatch(candidate) } }
        }
      } while (current(candidate) && (revision !== watchRevision || candidate.forceWatch))
    })().finally(() => { candidate.watchFlight = undefined })
  }
  function watchTicket(id: string) {
    // Child effects may register before the provider attaches (including StrictMode).
    const state = session.store.getState()
    const scope = state.status === 'authenticated' && !state.profilePending && !state.profileError ? state.scope : null
    if (!isGuid(id) || !scope) return () => {}
    id = id.toLowerCase()
    if (!wanted.has(id) && wanted.size >= 20) return () => {}
    const owner = Symbol()
    const entry = wanted.get(id) ?? { scope, owners: new Set<symbol>() }
    entry.owners.add(owner); wanted.set(id, entry); watchRevision++
    if (run) syncWatches(run)
    return () => {
      entry.owners.delete(owner)
      if (!entry.owners.size && wanted.get(id) === entry) wanted.delete(id)
      watchRevision++
      if (run) syncWatches(run)
    }
  }
  function resume() {
    if (!readyScope()) return
    if (run && current(run) && store.getState().status === 'connected') {
      inboxPending = true; for (const id of wanted.keys()) ticketPending.add(id)
      scheduleBatch(run); syncWatches(run, true)
    } else if (!run || !current(run)) { clearTimeout(retryTimer); retryTimer = undefined; reconcile() }
  }
  function attach() {
    owners++
    if (owners === 1) {
      unsubscribeStore = session.store.subscribe(observe); unsubscribeCredentials = session.subscribeCredentials(observe)
      observe()
    }
    let released = false
    return () => {
      if (released) return
      released = true
      if (--owners) return
      unsubscribeStore?.(); unsubscribeCredentials?.(); signature = ''; wanted.clear(); clearTimers(); clearBatch()
      if (run) void stop(run)
      status('idle')
    }
  }
  return { store, attach, watchTicket, resume }
}
function eventId(value: unknown): string | undefined {
  if (value && typeof value === 'object' && 'id' in value && typeof value.id === 'string' && isGuid(value.id)) return value.id.toLowerCase()
}
export type NotificationRealtime = ReturnType<typeof createNotificationRealtime>
