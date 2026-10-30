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
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, api: createTicketApi(services.client) }
  }
  const allocator = await login(input.allocator), leader = await login(input.leader), agent = await login(input.agent), requester = await login(input.requester)
  const signal = () => new AbortController().signal
  const lookup = { page: 1, pageSize: 1, search: '' }
  const create = title => requester.api.create({ title: input.prefix + title, description: 'Workflow integration fixture.', categoryId: input.categoryId, priority: 2 }, signal())
  let ticket = await create('Client workflow')
  const firstVersion = ticket.version
  assert.deepEqual((await requester.api.workflow(ticket.id, signal())).transitions, [6])
  await assert.rejects(requester.api.assignmentTeams(ticket.id, lookup, signal()), e => e.status === 403)
  await assert.rejects(leader.api.assignmentTeams(ticket.id, lookup, signal()), e => e.status === 404)
  await assert.rejects(allocator.client.get('/api/v1/support-teams'), e => e.status === 403)
  const teams = await allocator.api.assignmentTeams(ticket.id, { ...lookup, search: 'Team' }, signal())
  assert.equal(teams.items.length, 1); assert(teams.totalCount >= 2)
  const next = await allocator.api.assignmentTeams(ticket.id, { ...lookup, page: 2 }, signal())
  assert.equal(next.items.length, 1); assert.notEqual(next.items[0].id, teams.items[0].id)
  ticket = await allocator.api.assign(ticket.id, { teamId: input.teamA, agentId: null, version: ticket.version }, signal())
  assert.equal(ticket.status, 1)
  assert.deepEqual((await leader.api.workflow(ticket.id, signal())).transitions, [])
  const agents = await leader.api.assignmentAgents(ticket.id, input.teamB, { ...lookup, pageSize: 20 }, signal())
  assert(agents.items.some(user => user.id === input.agentId)); assert(!('email' in agents.items[0]))
  await assert.rejects(leader.api.assign(ticket.id, { teamId: input.teamB, agentId: input.agentId, version: firstVersion }, signal()), e => e.code === 'VersionConflict')
  ticket = await leader.api.assign(ticket.id, { teamId: input.teamB, agentId: input.agentId, version: ticket.version }, signal())
  assert.equal(ticket.status, 1)
  async function move(service, status, resolutionSummary = null) {
    const context = await service.api.workflow(ticket.id, signal())
    assert(context.transitions.includes(status))
    ticket = await service.api.transition(ticket.id, { status, resolutionSummary, version: context.version }, signal())
    assert.equal(ticket.status, status)
  }
  await move(agent, 2)
  await agent.api.comment(ticket.id, { content: 'Public response.', isInternal: false }, signal())
  await move(agent, 3)
  assert.deepEqual((await agent.api.workflow(ticket.id, signal())).transitions, [2])
  await move(agent, 2)
  await assert.rejects(agent.api.transition(ticket.id, { status: 4, resolutionSummary: '  ', version: ticket.version }, signal()), e => e.code === 'ResolutionRequired')
  await move(agent, 4, ' First resolution. ')
  assert.equal((await requester.api.detail(ticket.id, signal())).resolutionSummary, 'First resolution.')
  assert.deepEqual((await agent.api.workflow(ticket.id, signal())).transitions, [])
  await move(requester, 7)
  assert.equal((await requester.api.detail(ticket.id, signal())).resolutionSummary, null)
  await move(agent, 2); await move(agent, 4, 'Final resolution.'); await move(requester, 5)
  const history = await requester.api.activities(ticket.id, 1, signal(), false)
  assert(history.items.some(item => item.action === 'TicketAssigned'))
  assert(history.items.some(item => item.action === 'TicketStatusChanged'))
  assert.deepEqual((await leader.api.workflow(ticket.id, signal())).transitions, [])
  const completedId = ticket.id
  ticket = await create('Client cancel'); await move(requester, 6)
  assert.equal((await requester.api.workflow(ticket.id, signal())).canAssign, false)
  const result = { workflowCompleted: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runWorkflowBrowser } = await import('./ticket-workflow-browser.mjs')
    await runWorkflowBrowser({ ...input, completedId, create, services: { allocator, leader, requester, agent } })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/(?:ticket-workflow-(?:journey|browser)|release-acceptance-browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend workflow journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
