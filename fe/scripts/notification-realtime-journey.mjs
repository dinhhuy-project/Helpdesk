import assert from 'node:assert/strict'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { QueryObserver } from '@tanstack/react-query'
import { createServer } from 'vite'
const input = JSON.parse(await new Promise(resolve => {
  let value = ''; process.stdin.setEncoding('utf8'); process.stdin.on('data', chunk => { value += chunk }); process.stdin.on('end', () => resolve(value))
}))
const vite = await createServer({ configFile: false, mode: 'test', logLevel: 'silent', server: { middlewareMode: true, hmr: false } })
const clients = [], releases = [], observers = []
let shortConnection
try {
  const { createAppServices } = await vite.ssrLoadModule('/src/app/providers/app-services.ts')
  const { notificationConnectionFactory } = await vite.ssrLoadModule('/src/features/notifications/realtime/hub-connection.ts')
  const { createTicketApi } = await vite.ssrLoadModule('/src/features/tickets/api/ticket-api.ts')
  const { createNotificationApi } = await vite.ssrLoadModule('/src/features/notifications/api/notification-api.ts')
  const { createCatalogApi } = await vite.ssrLoadModule('/src/features/catalogs/api/catalog-api.ts')
  const { ticketKeys } = await vite.ssrLoadModule('/src/features/tickets/hooks/use-tickets.ts')
  const { notificationKeys } = await vite.ssrLoadModule('/src/features/notifications/hooks/use-notifications.ts')
  const { ApiError } = await vite.ssrLoadModule('/src/shared/api/api-error.ts')
  const signal = () => new AbortController().signal
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
  async function until(check) {
    for (let i = 0; i < 100; i++) { if (await check()) return; await pause(100) }
    assert.fail('Realtime state did not converge')
  }
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }, { connectionFactory: notificationConnectionFactory({ transport: HttpTransportType.LongPolling }) })
    clients.push(services); await services.session.login(credentials)
    return { ...services, api: createTicketApi(services.client), inbox: createNotificationApi(services.client), profile: services.session.store.getState().user }
  }
  const coordinator = await login(input.coordinator), requester = await login(input.requester), agent = await login(input.agent)
  const control = (service, action) => service.client.get(`/__tests/realtime/${action}`)
  const registry = async user => (await control(coordinator, 'connections')).filter(row => row.userId === user.profile.id)
  const create = title => requester.api.create({ title: input.prefix + title, description: 'Realtime private description.', categoryId: input.categoryId, priority: 2 }, signal())
  let ticket = await create('Client realtime')
  ticket = await coordinator.api.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, signal())
  function observe(service, key, queryFn) {
    const observer = new QueryObserver(service.queryClient, { queryKey: key, queryFn, retry: false, staleTime: 0, gcTime: 0 })
    const unsubscribe = observer.subscribe(() => {}); observers.push(unsubscribe); return observer
  }
  function observeTicket(service, internal) {
    const scope = service.session.store.getState().scope
    observe(service, ticketKeys.access(scope, ticket.id), () => service.api.access(ticket.id, signal()))
    observe(service, ticketKeys.comments(scope, ticket.id, { page: 1, pageSize: 20, isInternal: internal }), () => service.api.comments(ticket.id, { page: 1, pageSize: 20, isInternal: internal }, signal()))
    return scope
  }
  const scope = observeTicket(requester, false), agentScope = observeTicket(agent, true)
  const count = observe(requester, notificationKeys.count(scope), () => requester.inbox.count(signal()))
  const detach = requester.realtime.attach(); releases.push(detach)
  const unwatch = requester.realtime.watchTicket(ticket.id); releases.push(unwatch)
  releases.push(agent.realtime.attach(), agent.realtime.watchTicket(ticket.id))
  await until(async () => (await registry(requester)).some(row => row.tickets.includes(ticket.id)))
  await until(() => requester.realtime.store.getState().status === 'connected')
  const commentKey = ticketKeys.comments(scope, ticket.id, { page: 1, pageSize: 20, isInternal: false })
  await coordinator.api.comment(ticket.id, { content: 'Live public response.', isInternal: false }, signal())
  await control(coordinator, 'dispatch')
  await until(() => requester.queryClient.getQueryData(commentKey)?.items.some(row => row.content === 'Live public response.'))
  const beforeInternal = (await requester.inbox.count(signal())).count
  await coordinator.api.comment(ticket.id, { content: 'Internal realtime secret.', isInternal: true }, signal())
  await control(coordinator, 'dispatch')
  const internalKey = ticketKeys.comments(agentScope, ticket.id, { page: 1, pageSize: 20, isInternal: true })
  await until(() => agent.queryClient.getQueryData(internalKey)?.items.some(row => row.content === 'Internal realtime secret.'))
  assert.equal((await requester.inbox.count(signal())).count, beforeInternal)
  assert(!JSON.stringify(requester.queryClient.getQueryCache().getAll()).includes('Internal realtime secret.'))
  const unread = await requester.inbox.list({ page: 1, pageSize: 20, unread: true }, signal())
  await requester.inbox.markRead(unread.items[0].id, signal()); await control(coordinator, 'dispatch')
  await until(() => count.getCurrentResult().data?.count === beforeInternal - 1)
  const oldCredential = await requester.session.realtimeCredential(scope)
  const expired = await control(requester, 'expired-token')
  let first = true
  await requester.session.authorize(async token => {
    const response = await fetch(input.baseUrl + '/api/v1/users/me', { headers: { Authorization: `Bearer ${first ? expired.token : token}` } }); first = false
    if (response.status === 401) throw new ApiError('Expired', 'http', 401)
    assert.equal(response.status, 200)
  })
  assert.equal((await fetch(input.baseUrl + '/api/v1/users/me', { headers: { Authorization: `Bearer ${oldCredential.token}` } })).status, 401)
  await until(async () => (await registry(requester)).length === 1 && (await registry(requester))[0].tickets.includes(ticket.id))
  assert.notEqual((await requester.session.realtimeCredential(scope)).token, oldCredential.token)
  const previousId = (await registry(requester))[0].id
  await control(requester, 'disconnect')
  await until(async () => { const rows = await registry(requester); return rows.length === 1 && rows[0].id !== previousId && rows[0].tickets.includes(ticket.id) })
  // No live connection when this activity is dispatched: the next attach must recover it through REST.
  unwatch(); detach(); await until(async () => (await registry(requester)).length === 0)
  await coordinator.api.comment(ticket.id, { content: 'Persisted while disconnected.', isInternal: false }, signal()); await control(coordinator, 'dispatch')
  releases.push(requester.realtime.attach(), requester.realtime.watchTicket(ticket.id))
  await until(() => requester.queryClient.getQueryData(commentKey)?.items.some(row => row.content === 'Persisted while disconnected.'))
  const catalog = createCatalogApi(coordinator.client)
  async function setMembership(active) {
    const team = await catalog.detail('teams', input.teamId, signal())
    const members = await catalog.members(input.teamId, { page: 1, pageSize: 100 }, signal())
    const member = members.items.find(item => item.userId === input.agentId)
    await catalog.memberStatus(input.teamId, input.agentId, active, member.version, team.version, signal())
  }
  await setMembership(false); await control(coordinator, 'connections'); agent.realtime.resume()
  await until(() => agent.queryClient.getQueryState(ticketKeys.access(agentScope, ticket.id))?.status === 'error')
  assert.equal(agent.queryClient.getQueryData(internalKey), undefined)
  assert((await registry(agent)).every(row => !row.tickets.includes(ticket.id)))
  await setMembership(true)
  const short = await control(coordinator, 'short-token')
  shortConnection = new HubConnectionBuilder().withUrl(input.baseUrl + '/hubs/notifications', {
    transport: HttpTransportType.LongPolling, withCredentials: false, accessTokenFactory: () => short.token,
  }).configureLogging(LogLevel.None).build()
  let expiredClosed = false; shortConnection.onclose(() => { expiredClosed = true })
  await shortConnection.start(); await shortConnection.invoke('Ping')
  await until(() => expiredClosed)
  await requester.session.logout(); await until(async () => (await registry(requester)).length === 0)
  assert.equal(requester.realtime.store.getState().status, 'idle')
  const result = { realtimeVerified: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    observers.splice(0).forEach(unsubscribe => unsubscribe()); releases.splice(0).forEach(release => release())
    await requester.session.login(input.requester)
    const { runRealtimeBrowser } = await import('./notification-realtime-browser.mjs')
    await runRealtimeBrowser({ ...input, signal, create, setMembership, control, registry, services: { coordinator, agent, requester } })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/notification-realtime-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend realtime journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  observers.forEach(unsubscribe => unsubscribe()); releases.forEach(release => release())
  await shortConnection?.stop().catch(() => {})
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
