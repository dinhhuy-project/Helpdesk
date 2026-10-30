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
  const { createSlaApi } = await vite.ssrLoadModule('/src/features/sla/api/sla-api.ts')
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, api: createTicketApi(services.client), sla: createSlaApi(services.client) }
  }
  const manager = await login(input.manager), allocator = await login(input.allocator), agent = await login(input.agent)
  const requester = await login(input.requester), outsider = await login(input.outsider)
  const signal = () => new AbortController().signal
  const create = title => requester.api.create({ title: input.prefix + title, description: 'SLA integration fixture.', categoryId: input.categoryId, priority: 2 }, signal())
  const without = await create('No policy')
  assert.equal((await requester.sla.ticket(without.id, signal())).sla, null)
  await assert.rejects(requester.sla.list({ page: 1, pageSize: 20 }, signal()), e => e.status === 403)
  await assert.rejects(manager.api.detail(without.id, signal()), e => e.status === 403)
  let policy = await manager.sla.create({ name: input.prefix + 'Original', priority: 2, firstResponseMinutes: 60, resolutionMinutes: 240 }, signal())
  assert.equal(policy.isActive, true)
  let ticket = await create('Client snapshot')
  const original = (await requester.sla.ticket(ticket.id, signal())).sla
  assert(original); assert.equal(original.policyName, policy.name)
  assert.equal(original.firstResponseMinutes, 60); assert.equal(original.resolutionMinutes, 240)
  assert.equal(Date.parse(original.firstResponseDueAt) - Date.parse(original.appliedAt), 60 * 60_000)
  assert.equal(Date.parse(original.resolutionDueAt) - Date.parse(original.appliedAt), 240 * 60_000)
  const oldVersion = policy.version
  policy = await manager.sla.update(policy.id, { name: input.prefix + 'Updated', priority: 2, firstResponseMinutes: 30, resolutionMinutes: 120 }, policy.version, signal())
  await assert.rejects(manager.sla.status(policy.id, false, oldVersion, signal()), e => e.code === 'VersionConflict')
  await assert.rejects(manager.sla.create({ name: input.prefix + 'Duplicate', priority: 2, firstResponseMinutes: 1, resolutionMinutes: 2 }, signal()), e => e.code === 'ActivePolicyExists')
  assert.deepEqual((await requester.sla.ticket(ticket.id, signal())).sla, original)
  assert.equal((await requester.sla.ticket(without.id, signal())).sla, null)
  const updated = await create('New snapshot')
  assert.equal((await requester.sla.ticket(updated.id, signal())).sla.firstResponseMinutes, 30)
  policy = await manager.sla.status(policy.id, false, policy.version, signal())
  const inactive = await create('Inactive policy')
  assert.equal((await requester.sla.ticket(inactive.id, signal())).sla, null)
  policy = await manager.sla.status(policy.id, true, policy.version, signal())
  assert.equal((await requester.sla.ticket(inactive.id, signal())).sla, null)
  const listing = await manager.sla.list({ page: 1, pageSize: 1 }, signal())
  assert.equal(listing.items.length, 1); assert(listing.totalCount >= 1)
  await assert.rejects(outsider.sla.ticket(ticket.id, signal()), e => e.status === 404)
  await assert.rejects(agent.sla.ticket(ticket.id, signal()), e => e.status === 404)
  ticket = await allocator.api.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, signal())
  async function move(service, status, resolutionSummary = null) {
    const context = await service.api.workflow(ticket.id, signal())
    ticket = await service.api.transition(ticket.id, { status, resolutionSummary, version: context.version }, signal())
  }
  await move(agent, 2); await move(agent, 3)
  assert.equal((await agent.sla.ticket(ticket.id, signal())).sla.resolutionDueAt, original.resolutionDueAt)
  await agent.api.comment(ticket.id, { content: 'Internal note does not count.', isInternal: true }, signal())
  assert.equal((await requester.sla.ticket(ticket.id, signal())).sla.firstRespondedAt, null)
  await agent.api.comment(ticket.id, { content: 'First public reply.', isInternal: false }, signal())
  const responded = (await requester.sla.ticket(ticket.id, signal())).sla.firstRespondedAt
  assert(responded)
  await move(agent, 2); await move(agent, 4, 'Resolved.')
  const resolved = await requester.sla.ticket(ticket.id, signal())
  assert.equal(resolved.status, 4); assert(resolved.sla.resolvedAt); assert(Number.isFinite(Date.parse(resolved.asOf)))
  await move(requester, 7)
  const reopened = (await requester.sla.ticket(ticket.id, signal())).sla
  assert.equal(reopened.resolvedAt, null); assert.equal(reopened.firstRespondedAt, responded)
  assert.equal(reopened.resolutionDueAt, original.resolutionDueAt)
  await move(agent, 2); await move(agent, 4, 'Resolved again.'); await move(requester, 5)
  const result = { slaVerified: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runSlaBrowser } = await import('./ticket-sla-browser.mjs')
    await runSlaBrowser({ ...input, policy, original, create, signal, services: { manager, requester, allocator, agent } })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/ticket-sla-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend SLA journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
