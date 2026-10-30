import { QueryObserver } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createAuthSession } from '../../auth/session/auth-session'
import { createQueryClient } from '../../../app/providers/query-client'
import { createMockAuthApi, deferred, tokenPair, userA } from '../../../test/auth-fixtures'
import { ApiError } from '../../../shared/api/api-error'
import type { NotificationConnection } from './hub-connection'
import { createNotificationRealtime } from './notification-realtime'
import { ticketKeys } from '../../tickets/hooks/use-tickets'
import { notificationKeys } from '../hooks/use-notifications'
import { reportKeys } from '../../dashboard/hooks/use-reports'

const id = '10000000-0000-0000-0000-000000000001', other = '10000000-0000-0000-0000-000000000002'
const cleanups: (() => void)[] = []
beforeEach(() => { vi.useFakeTimers() })
afterEach(async () => { cleanups.splice(0).forEach(cleanup => cleanup()); await drain(); vi.useRealTimers() })
async function drain() { for (let i = 0; i < 60; i++) await Promise.resolve() }
class Connection implements NotificationConnection {
  handlers = new Map<string, (...args: unknown[]) => void>()
  close?: () => void; reconnecting?: () => void; reconnected?: () => void
  startGate?: Promise<void>; watchGate?: Promise<void>; stopped = false
  failStart = false; denyWatch = false
  starts = 0; stops = 0; token = ''
  calls: { method: string; id?: string }[] = []
  tokenFactory: () => Promise<string>
  constructor(tokenFactory: () => Promise<string>) { this.tokenFactory = tokenFactory }
  async start() {
    this.starts++
    expect([...this.handlers.keys()]).toEqual(['NotificationsChanged', 'NotificationChanged', 'TicketChanged'])
    this.token = await this.tokenFactory()
    await this.startGate
    if (this.failStart || this.stopped) throw new Error('Connection not started')
  }
  async stop() { this.stopped = true; this.stops++; this.close?.() }
  async invoke<T>(method: string, selectedId?: string): Promise<T> {
    this.calls.push({ method, id: selectedId })
    if (method === 'WatchTicket') { await this.watchGate; if (this.denyWatch) throw new Error('NotFound') }
    return undefined as T
  }
  on(event: string, callback: (...args: unknown[]) => void) { this.handlers.set(event, callback) }
  off(event: string) { this.handlers.delete(event) }
  onclose(callback: () => void) { this.close = callback }
  onreconnecting(callback: () => void) { this.reconnecting = callback }
  onreconnected(callback: () => void) { this.reconnected = callback }
  emit(event: string, payload?: unknown) { this.handlers.get(event)?.(payload) }
}
async function fixture(loggedIn = true) {
  const api = createMockAuthApi(), queries = createQueryClient()
  queries.setDefaultOptions({ queries: { retry: false } })
  const session = createAuthSession({ api, onScopeChange: () => { void queries.cancelQueries(); queries.clear() } })
  if (loggedIn) await session.login({ email: userA.email, password: 'test-only' })
  const connections: Connection[] = []
  let configure: (connection: Connection) => void = () => {}
  const factory = vi.fn((_url: string, getToken: () => Promise<string>) => {
    const connection = new Connection(getToken); configure(connection); connections.push(connection); return connection
  })
  const realtime = createNotificationRealtime({ session, queries, url: '/hubs/notifications', connectionFactory: factory })
  const detaches: (() => void)[] = []
  cleanups.push(() => { detaches.forEach(detach => detach()); queries.clear() })
  const attach = () => { const detach = realtime.attach(); detaches.push(detach); return detach }
  const observe = <T,>(key: readonly unknown[], queryFn: () => Promise<T>) => {
    const observer = new QueryObserver(queries, { queryKey: key, queryFn, retry: false })
    const unsubscribe = observer.subscribe(() => {}); cleanups.push(unsubscribe); return observer
  }
  return { api, queries, session, connections, factory, realtime, attach, observe, configure: (value: typeof configure) => { configure = value } }
}
describe('session-owned SignalR lifecycle', () => {
  it('waits for authentication and profile verification, registers handlers before start and has one connection per owner set', async () => {
    const f = await fixture(false); f.attach(); await drain(); expect(f.factory).not.toHaveBeenCalled()
    const me = deferred<typeof userA>(); f.api.me.mockReturnValueOnce(me.promise)
    const login = f.session.login({ email: userA.email, password: 'test-only' }); await drain()
    expect(f.factory).not.toHaveBeenCalled(); me.resolve(userA); await login; await drain()
    f.attach(); await drain()
    expect(f.connections).toHaveLength(1); expect(f.connections[0]!.token).toBe('access-a')
    expect(f.realtime.store.getState().status).toBe('connected')
  })
  it('stops a pending start immediately on logout and ignores late callbacks', async () => {
    const f = await fixture(); const gate = deferred<void>(); f.configure(c => { c.startGate = gate.promise })
    f.attach(); await drain(); const old = f.connections[0]!
    const late = old.handlers.get('NotificationChanged')!
    await f.session.logout(); await drain(); expect(old.stops).toBeGreaterThan(0)
    gate.resolve(); await drain(); late({ id })
    await vi.advanceTimersByTimeAsync(200)
    expect(f.realtime.store.getState().status).toBe('idle'); expect(f.queries.getQueryCache().getAll()).toHaveLength(0)
  })
  it('handles StrictMode detach/attach while start is pending without overlapping connections', async () => {
    const f = await fixture(); const gate = deferred<void>(); f.configure(c => { c.startGate = gate.promise })
    const detach = f.attach(); await drain(); detach(); f.attach(); gate.resolve(); await drain()
    expect(f.connections[0]!.stopped).toBe(true)
    expect(f.connections.filter(c => !c.stopped)).toHaveLength(1)
  })
  it('serializes rotation with concurrent REST and hub credential requests and reconnects with the new token', async () => {
    const f = await fixture(); f.attach(); await drain(); const old = f.connections[0]!
    const gate = deferred<ReturnType<typeof tokenPair>>(); f.api.refresh.mockReturnValueOnce(gate.promise)
    const before = f.session.credentialRevision()
    const rest = f.session.authorize(async token => { if (token === 'access-a') throw new ApiError('Expired', 'http', 401); return 'ok' })
    await drain(); const credential = f.session.realtimeCredential(f.session.store.getState().scope!)
    gate.resolve(tokenPair('b')); expect(await rest).toBe('ok'); expect((await credential).token).toBe('access-b'); await drain()
    expect(f.api.refresh).toHaveBeenCalledTimes(1); expect(f.session.credentialRevision()).toBeGreaterThan(before)
    expect(old.stopped).toBe(true); expect(f.connections.at(-1)!.token).toBe('access-b')
    expect(f.connections.filter(c => !c.stopped)).toHaveLength(1)
    expect(JSON.stringify(f.realtime.store.getState())).not.toContain('access-')
    expect(JSON.stringify(f.session.store.getState())).not.toContain('access-')
  })
  it('renews the access credential before expiry without an HTTP request from a page', async () => {
    const f = await fixture(); f.api.refresh.mockImplementation(async () => tokenPair('b')); f.attach(); await drain()
    await vi.advanceTimersByTimeAsync(891_100)
    expect(f.api.refresh).toHaveBeenCalledTimes(1)
    expect(f.connections[0]!.stopped).toBe(true); expect(f.connections.at(-1)!.token).toBe('access-b')
  })
  it('retries initial start failures with backoff and stops retrying when detached', async () => {
    const f = await fixture(); f.configure(c => { c.failStart = true }); const detach = f.attach(); await drain()
    expect(f.realtime.store.getState().status).toBe('offline'); expect(f.factory).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(2_001); expect(f.factory).toHaveBeenCalledTimes(2)
    detach(); await vi.advanceTimersByTimeAsync(60_000); expect(f.factory).toHaveBeenCalledTimes(2)
  })
  it('invalidates old callbacks and watches when permissions change', async () => {
    const f = await fixture(); f.realtime.watchTicket(id); f.attach(); await drain()
    const old = f.connections[0]!, late = old.handlers.get('TicketChanged')!
    f.api.me.mockResolvedValue({ ...userA, permissions: [] }); await f.session.refreshProfile({ background: true }); await drain()
    late({ id }); await vi.advanceTimersByTimeAsync(200)
    expect(old.stopped).toBe(true)
    expect(f.connections.at(-1)!.calls.filter(c => c.method === 'WatchTicket')).toHaveLength(0)
    expect(f.queries.getQueryCache().getAll()).toHaveLength(0)
  })
  it('does not revive a logged-out connection when an in-flight rotation completes', async () => {
    const f = await fixture(); f.attach(); await drain()
    const gate = deferred<ReturnType<typeof tokenPair>>(); f.api.refresh.mockReturnValueOnce(gate.promise)
    const request = f.session.authorize(async () => { throw new ApiError('Expired', 'http', 401) }).catch(() => {})
    await drain(); await f.session.logout(); gate.resolve(tokenPair('b')); await request; await drain()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(f.connections).toHaveLength(1); expect(f.connections[0]!.stopped).toBe(true)
    expect(f.session.store.getState().status).toBe('anonymous'); expect(f.realtime.store.getState().status).toBe('idle')
  })
  it('lets failed REST verification end an inactive session instead of retrying the hub forever', async () => {
    const f = await fixture(); f.configure(c => { c.failStart = true })
    f.api.me.mockRejectedValue(new ApiError('Inactive', 'http', 401)); f.api.refresh.mockRejectedValue(new ApiError('Inactive', 'http', 401))
    f.attach(); await drain(); await vi.advanceTimersByTimeAsync(60_000)
    expect(f.session.store.getState().status).toBe('anonymous'); expect(f.factory).toHaveBeenCalledTimes(1)
  })
  it('restarts after SDK reconnect is exhausted and stops the previous instance first', async () => {
    const f = await fixture(); f.attach(); await drain(); const old = f.connections[0]!
    old.close?.(); await vi.advanceTimersByTimeAsync(2_001)
    expect(old.stopped).toBe(true); expect(f.connections.filter(c => !c.stopped)).toHaveLength(1)
    expect(f.realtime.store.getState().status).toBe('connected')
  })
})
describe('Ticket watches and REST reconciliation', () => {
  it('retains one subscription per Ticket and unwatches only after the last owner leaves', async () => {
    const f = await fixture(); const first = f.realtime.watchTicket(id), second = f.realtime.watchTicket(id)
    f.attach(); await drain(); const connection = f.connections[0]!
    expect(connection.calls.filter(c => c.method === 'WatchTicket')).toEqual([{ method: 'WatchTicket', id }])
    first(); await drain(); expect(connection.calls.some(c => c.method === 'UnwatchTicket')).toBe(false)
    second(); await drain(); expect(connection.calls.at(-1)).toEqual({ method: 'UnwatchTicket', id })
  })
  it('unwatches a Ticket released while its WatchTicket invocation was pending', async () => {
    const f = await fixture(); const gate = deferred<void>(); f.configure(c => { c.watchGate = gate.promise })
    const release = f.realtime.watchTicket(id); f.attach(); await drain(); release(); gate.resolve(); await drain()
    expect(f.connections[0]!.calls.at(-1)).toEqual({ method: 'UnwatchTicket', id })
  })
  it('rewatches desired Tickets and refetches durable state after reconnect', async () => {
    const f = await fixture(), scope = f.session.store.getState().scope!
    const count = vi.fn(async () => ({ count: 1 })); f.observe(notificationKeys.count(scope), count)
    const report = vi.fn(async () => ({ total: 7 })); f.observe([...reportKeys.root(scope), 'dashboard'], report)
    f.realtime.watchTicket(id); f.attach(); await drain(); await vi.advanceTimersByTimeAsync(200)
    const before = count.mock.calls.length, reportsBefore = report.mock.calls.length, connection = f.connections[0]!
    connection.reconnecting?.(); expect(f.realtime.store.getState().status).toBe('reconnecting')
    connection.reconnected?.(); await drain(); await vi.advanceTimersByTimeAsync(200)
    expect(count.mock.calls.length).toBeGreaterThan(before)
    expect(report.mock.calls.length).toBeGreaterThan(reportsBefore)
    expect(connection.calls.filter(c => c.method === 'WatchTicket')).toHaveLength(2)
  })
  it('batches duplicate ID events and never applies event data or computes a count locally', async () => {
    const f = await fixture(), scope = f.session.store.getState().scope!
    const count = vi.fn(async () => ({ count: 7 })); f.observe(notificationKeys.count(scope), count)
    f.attach(); await drain(); await vi.advanceTimersByTimeAsync(200); const before = count.mock.calls.length
    for (let i = 0; i < 10; i++) f.connections[0]!.emit('NotificationChanged', { id, count: 999, content: 'private text' })
    await vi.advanceTimersByTimeAsync(200)
    expect(count).toHaveBeenCalledTimes(before + 1); expect(f.queries.getQueryData(notificationKeys.count(scope))).toEqual({ count: 7 })
    expect(JSON.stringify(f.queries.getQueryCache().getAll())).not.toContain('private text')
    expect(f.queries.getQueryData(notificationKeys.detail(scope, id))).toBeUndefined()
    count.mockResolvedValue({ count: 2 }); f.connections[0]!.emit('NotificationChanged', { id }); await vi.advanceTimersByTimeAsync(200)
    expect(f.queries.getQueryData(notificationKeys.count(scope))).toEqual({ count: 2 }) // The same ID can signal mark-read later.
  })
  it('ignores malformed IDs and unwatched Ticket events', async () => {
    const f = await fixture(); f.attach(); await drain(); await vi.advanceTimersByTimeAsync(200)
    const invalidate = vi.spyOn(f.queries, 'invalidateQueries')
    for (const value of [null, {}, { id: '../secret' }, { id: 7 }]) f.connections[0]!.emit('NotificationChanged', value)
    f.connections[0]!.emit('TicketChanged', { id: other }); await vi.advanceTimersByTimeAsync(200)
    expect(invalidate).not.toHaveBeenCalled()
  })
  it('revalidates access before private queries and removes old private caches after access is lost', async () => {
    const f = await fixture(), scope = f.session.store.getState().scope!
    let allowed = true
    f.observe(ticketKeys.access(scope, id), async () => { if (!allowed) throw new ApiError('NotFound', 'http', 404); return { canRead: true } })
    await drain(); f.realtime.watchTicket(id); f.attach(); await drain(); await vi.advanceTimersByTimeAsync(200)
    f.queries.setQueryData(ticketKeys.detail(scope, id), { secret: 'old private Ticket' })
    allowed = false; f.connections[0]!.emit('TicketChanged', { id }); await vi.advanceTimersByTimeAsync(250)
    expect(f.queries.getQueryData(ticketKeys.detail(scope, id))).toBeUndefined()
    expect(f.queries.getQueryState(ticketKeys.access(scope, id))?.status).toBe('error')
  })
  it('checks resource access on rejected watch even if profile permission strings did not change', async () => {
    const f = await fixture(), scope = f.session.store.getState().scope!
    const access = vi.fn(async () => { throw new ApiError('NotFound', 'http', 404) })
    f.observe(ticketKeys.access(scope, id), access); f.configure(c => { c.denyWatch = true })
    f.realtime.watchTicket(id); f.attach(); await drain(); await vi.advanceTimersByTimeAsync(200)
    expect(access.mock.calls.length).toBeGreaterThan(1)
  })
  it('does not exceed 20 distinct desired watches', async () => {
    const f = await fixture()
    for (let i = 1; i <= 21; i++) f.realtime.watchTicket(`10000000-0000-0000-0000-${String(i).padStart(12, '0')}`)
    f.attach(); await drain(); expect(f.connections[0]!.calls.filter(c => c.method === 'WatchTicket')).toHaveLength(20)
  })
})
