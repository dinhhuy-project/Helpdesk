import assert from 'node:assert/strict'
import { createServer } from 'vite'
const input = JSON.parse(await new Promise(resolve => {
  let value = ''; process.stdin.setEncoding('utf8'); process.stdin.on('data', chunk => { value += chunk }); process.stdin.on('end', () => resolve(value))
}))
const vite = await createServer({ configFile: false, mode: 'test', logLevel: 'silent', server: { middlewareMode: true, hmr: false } })
const clients = []
try {
  const { createAppServices } = await vite.ssrLoadModule('/src/app/providers/app-services.ts')
  const { createTicketApi } = await vite.ssrLoadModule('/src/features/tickets/api/ticket-api.ts')
  const { createNotificationApi } = await vite.ssrLoadModule('/src/features/notifications/api/notification-api.ts')
  const { createCatalogApi } = await vite.ssrLoadModule('/src/features/catalogs/api/catalog-api.ts')
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, api: createTicketApi(services.client), inbox: createNotificationApi(services.client) }
  }
  const coordinator = await login(input.coordinator), agent = await login(input.agent)
  const requester = await login(input.requester), outsider = await login(input.outsider)
  const signal = () => new AbortController().signal
  const search = { page: 1, pageSize: 100 }
  const create = title => requester.api.create({ title: input.prefix + title, description: 'Private notification journey description.', categoryId: input.categoryId, priority: 2 }, signal())
  assert.equal((await requester.inbox.count(signal())).count, 0)
  let ticket = await create('Client inbox')
  assert.equal((await requester.inbox.count(signal())).count, 0) // Actor does not receive its own activity.
  ticket = await coordinator.api.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, signal())
  await coordinator.api.comment(ticket.id, { content: 'Private public-comment body, never embedded in notification.', isInternal: false }, signal())
  await coordinator.api.comment(ticket.id, { content: 'Internal notification journey secret.', isInternal: true }, signal())
  const all = await requester.inbox.list(search, signal())
  assert.equal(all.totalCount, 2); assert(all.items.every(item => !item.isInternal))
  assert.equal((await requester.inbox.count(signal())).count, 2)
  assert(!JSON.stringify(all).includes('Private public-comment body'))
  const first = await requester.inbox.list({ page: 1, pageSize: 1 }, signal())
  const second = await requester.inbox.list({ page: 2, pageSize: 1 }, signal())
  assert.notEqual(first.items[0].id, second.items[0].id)
  const selected = first.items[0]
  const receipt = await requester.inbox.markRead(selected.id, signal())
  assert(receipt.readAt); assert.equal(receipt.ticketId, ticket.id)
  assert.equal((await requester.inbox.markRead(selected.id, signal())).readAt, receipt.readAt)
  assert.equal((await requester.inbox.count(signal())).count, 1)
  assert.equal((await requester.inbox.list({ ...search, unread: true }, signal())).totalCount, 1)
  assert.equal((await requester.inbox.list({ ...search, unread: false }, signal())).items[0].id, selected.id)
  assert.equal((await requester.inbox.detail(selected.id, signal())).readAt, receipt.readAt)
  await assert.rejects(outsider.inbox.detail(selected.id, signal()), e => e.status === 404)
  await assert.rejects(outsider.inbox.markRead(selected.id, signal()), e => e.status === 404)
  const teamInbox = await agent.inbox.list(search, signal())
  const internal = teamInbox.items.find(item => item.isInternal)
  assert(internal); assert(!JSON.stringify(teamInbox).includes('Internal notification journey secret.'))
  await assert.rejects(requester.inbox.detail(internal.id, signal()), e => e.status === 404)
  const catalog = createCatalogApi(coordinator.client)
  async function setMembership(active) {
    const team = await catalog.detail('teams', input.teamId, signal())
    const members = await catalog.members(input.teamId, search, signal())
    const member = members.items.find(item => item.userId === input.agentId)
    assert(member)
    await catalog.memberStatus(input.teamId, input.agentId, active, member.version, team.version, signal())
  }
  await setMembership(false)
  assert.equal((await agent.inbox.count(signal())).count, 0)
  assert.equal((await agent.inbox.list(search, signal())).totalCount, 0)
  await assert.rejects(agent.inbox.detail(internal.id, signal()), e => e.status === 404)
  await assert.rejects(agent.inbox.markRead(internal.id, signal()), e => e.status === 404)
  await setMembership(true)
  assert((await agent.inbox.list(search, signal())).items.some(item => item.id === internal.id))
  const result = { notificationsVerified: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runNotificationBrowser } = await import('./notification-inbox-browser.mjs')
    await runNotificationBrowser({ ...input, create, signal, setMembership, services: { coordinator, requester, agent, outsider } })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/notification-inbox-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend notification journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
