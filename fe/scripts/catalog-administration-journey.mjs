// Load the real frontend typed clients, authentication and cache against disposable application/PostgreSQL.
import assert from 'node:assert/strict'
import { createServer } from 'vite'
const input = JSON.parse(await new Promise(resolve => {
  let value = ''; process.stdin.setEncoding('utf8'); process.stdin.on('data', chunk => { value += chunk }); process.stdin.on('end', () => resolve(value))
}))
const vite = await createServer({ configFile: false, mode: 'test', logLevel: 'silent', server: { middlewareMode: true, hmr: false } })
const clients = []
try {
  const { createAppServices } = await vite.ssrLoadModule('/src/app/providers/app-services.ts')
  const { createCatalogApi } = await vite.ssrLoadModule('/src/features/catalogs/api/catalog-api.ts')
  const { createTicketApi } = await vite.ssrLoadModule('/src/features/tickets/api/ticket-api.ts')
  const { getTicketCategoryLookup } = await vite.ssrLoadModule('/src/features/tickets/api/ticket-category-api.ts')
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, api: createCatalogApi(services.client), tickets: createTicketApi(services.client) }
  }
  const admin = await login(input.admin), leader = await login(input.leader), requester = await login(input.requester)
  const search = { page: 1, pageSize: 20, sortBy: 'name', descending: false }
  const lookup = { page: 1, pageSize: 20, search: input.prefix }
  const signal = () => new AbortController().signal
  let department = await admin.api.create('departments', { name: `${input.prefix} Department`, description: null })
  await assert.rejects(admin.api.create('departments', { name: department.name.toUpperCase(), description: null }), e => e.code === 'NameInUse')
  assert((await admin.api.teamDepartments(lookup)).items.some(x => x.id === department.id))
  let team = await admin.api.create('teams', { name: `${input.prefix} Team`, description: null, departmentId: department.id })
  assert.equal(team.departmentName, department.name)
  const outside = await admin.api.create('teams', { name: `${input.prefix} Outside`, description: null, departmentId: department.id })
  let root = await admin.api.create('categories', { name: `${input.prefix} Root`, description: null, parentId: null })
  let child = await admin.api.create('categories', { name: `${input.prefix} Child`, description: null, parentId: root.id })
  assert.equal(child.parentName, root.name)
  await assert.rejects(admin.api.update('categories', root.id, { name: root.name, description: null, parentId: child.id, version: root.version }), e => e.code === 'CategoryCycle')
  assert.equal((await leader.api.teamAccess()).canCreate, false)
  let added = await admin.api.addMember(team.id, input.leaderId, team.version)
  assert.equal(added.member.canBeLeader, true)
  added = await admin.api.leadership(team.id, input.leaderId, true, added.member.version, added.team.version)
  team = added.team
  assert.equal((await leader.api.teamAccess(team.id)).canManageLeadership, false)
  assert((await leader.api.list('teams', search)).items.some(x => x.id === team.id))
  await assert.rejects(leader.api.candidates(outside.id, lookup), e => e.status === 404)
  await assert.rejects(leader.api.create('teams', { name: `${input.prefix} Denied`, description: null, departmentId: department.id }), e => e.status === 403)
  await assert.rejects(leader.client.get('/api/v1/users'), e => e.status === 403)
  const candidates = await leader.api.candidates(team.id, lookup)
  assert(candidates.items.some(x => x.id === input.candidateId))
  const staleTeamVersion = team.version
  added = await leader.api.addMember(team.id, input.candidateId, team.version)
  assert.equal(added.member.canBeLeader, false)
  await assert.rejects(leader.api.memberStatus(team.id, input.candidateId, false, added.member.version, staleTeamVersion), e => e.code === 'VersionConflict')
  await assert.rejects(leader.api.leadership(team.id, input.candidateId, true, added.member.version, added.team.version), e => e.status === 403)
  await leader.api.removeMember(team.id, input.candidateId, added.member.version, added.team.version)
  team = await leader.api.detail('teams', team.id)
  const removed = (await leader.api.members(team.id, { page: 1, pageSize: 20, isActive: false })).items.find(x => x.userId === input.candidateId)
  assert.equal(removed.isActive, false); assert.equal(removed.joinedAt, added.member.joinedAt)
  assert(!(await leader.api.candidates(team.id, lookup)).items.some(x => x.id === input.candidateId))
  added = await leader.api.memberStatus(team.id, input.candidateId, true, removed.version, team.version)
  team = added.team
  const categories = await getTicketCategoryLookup(requester.client, { ...lookup, rootsOnly: false }, signal())
  assert(categories.items.some(x => x.id === child.id))
  let ticket = await requester.tickets.create({ title: `${input.prefix} Ticket`, description: 'Created with newly administered reference data.', categoryId: child.id, priority: 2 }, signal())
  await admin.client.post(`/api/v1/tickets/${ticket.id}/assign`, { teamId: team.id, agentId: input.candidateId, version: ticket.version })
  ticket = await requester.tickets.detail(ticket.id, signal())
  assert.equal(ticket.assignedTeamId, team.id); assert.equal(ticket.assignedAgentId, input.candidateId)
  child = await admin.api.update('categories', child.id, { name: child.name, description: null, parentId: null, version: child.version })
  assert.equal(child.parentId, null)
  child = await admin.api.update('categories', child.id, { name: child.name, description: null, parentId: root.id, version: child.version })
  department = await admin.api.status('departments', department.id, false, department.version)
  await assert.rejects(leader.api.detail('teams', team.id), e => e.status === 404)
  const historicalTeam = await admin.api.detail('teams', team.id)
  assert.equal(historicalTeam.isActive, true); assert.equal(historicalTeam.departmentIsActive, false)
  root = await admin.api.status('categories', root.id, false, root.version)
  child = await admin.api.detail('categories', child.id)
  assert.equal(child.isActive, true); assert.equal(child.isAvailable, false); assert.equal(child.canActivate, false)
  assert(!(await getTicketCategoryLookup(requester.client, { ...lookup, rootsOnly: false }, signal())).items.some(x => x.id === child.id))
  ticket = await requester.tickets.detail(ticket.id, signal())
  assert.equal(ticket.categoryId, child.id); assert.equal(ticket.assignedTeamId, team.id)
  const result = { ticketHistoryPreserved: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runCatalogBrowser } = await import('./catalog-administration-browser.mjs')
    await runCatalogBrowser(input)
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/catalog-administration-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend catalog journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
