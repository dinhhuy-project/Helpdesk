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
  const { createReportApi } = await vite.ssrLoadModule('/src/features/dashboard/api/report-api.ts')
  const { createCatalogApi } = await vite.ssrLoadModule('/src/features/catalogs/api/catalog-api.ts')
  const { defaultReportSearch, parseReportSearch, reportSearchParams } = await vite.ssrLoadModule('/src/features/dashboard/lib/report-search.ts')
  const signal = () => new AbortController().signal
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, tickets: createTicketApi(services.client), reports: createReportApi(services.client) }
  }
  const coordinator = await login(input.coordinator), requester = await login(input.requester)
  const agent = await login(input.agent), outsider = await login(input.outsider), noRead = await login(input.noRead)
  const filter = { createdFrom: '2026-01-01T00:00:00.000Z', createdTo: '2026-01-04T00:00:00.000Z' }
  const search = { ...defaultReportSearch, ...filter, pageSize: 1 }
  async function parity(client, count, extra = {}) {
    const cohort = { ...filter, ...extra }
    const [dashboard, daily, teams, agents, list] = await Promise.all([
      client.reports.dashboard(cohort, signal()), client.reports.daily(cohort, signal()),
      client.reports.groups({ ...search, ...cohort }, signal()), client.reports.groups({ ...search, ...cohort, view: 'agents' }, signal()),
      client.tickets.list({ ...cohort, page: 1, pageSize: 1, sortBy: 'createdAt', descending: true }, signal()),
    ])
    assert.equal(dashboard.tickets.total, count)
    // Compare server totalCount and aggregates for the SAME cohort. Never sum Ticket list pages.
    assert.equal(list.totalCount, count)
    assert.equal(daily.days.reduce((total, day) => total + day.createdTickets, 0), count)
    assert.equal(dashboard.byStatus.length, 7); assert.equal(dashboard.byPriority.length, 4)
    assert(dashboard.byStatus.some(row => row.count === 0)); assert(dashboard.byPriority.some(row => row.count === 0))
    for (const response of [dashboard, daily, teams, agents]) {
      assert.equal(response.period.createdFrom.slice(0, 10), '2026-01-01')
      assert.equal(response.period.createdTo.slice(0, 10), '2026-01-04')
      assert(!JSON.stringify(response).includes('private-description-'))
    }
    return { dashboard, daily, teams, agents }
  }
  const own = await parity(requester, 3)
  await parity(agent, 2); await parity(coordinator, 5); const outside = await parity(outsider, 2)
  assert.equal(outside.dashboard.sla.firstResponse.metPercent, null)
  assert.equal(outside.dashboard.sla.resolution.averageMinutes, null)
  assert.deepEqual(own.daily.days.map(row => row.createdTickets), [2, 1, 0])
  assert.equal(own.teams.groups.totalCount, 2); assert.equal(own.teams.groups.items.length, 1)
  assert.equal(own.teams.groups.items[0].id, input.teamId)
  assert.equal(own.teams.groups.items[0].tickets.total, 2)
  const next = await requester.reports.groups({ ...search, page: 2 }, signal())
  assert.equal(next.groups.items[0].id, null); assert.equal(next.groups.items[0].name, 'Chưa phân công')
  const inactive = await coordinator.reports.groups({ ...search, teamId: input.hiddenTeamId }, signal())
  assert.equal(inactive.groups.items[0].isActive, false)
  assert.equal(own.dashboard.sla.withSla, 2); assert.equal(own.dashboard.sla.withoutSla, 1)
  assert.equal(own.dashboard.sla.firstResponse.metPercent, 50); assert.equal(own.dashboard.sla.resolution.metPercent, 50)
  assert.equal(own.dashboard.sla.firstResponse.averageMinutes, 10); assert.equal(own.dashboard.sla.resolution.averageMinutes, 20)
  await parity(requester, 2, { teamId: input.teamId, agentId: input.agentId })
  await parity(requester, 1, { status: 3, priority: 2 })
  const hidden = await parity(requester, 0, { teamId: input.hiddenTeamId })
  assert.equal(hidden.dashboard.sla.firstResponse.metPercent, null); assert.equal(hidden.teams.groups.items.length, 0)
  for (const read of [() => noRead.reports.dashboard(filter, signal()), () => noRead.reports.groups(search, signal()), () => noRead.reports.daily(filter, signal())])
    await assert.rejects(read, error => error.status === 403)
  await assert.rejects(requester.reports.dashboard({ ...filter, createdTo: filter.createdFrom }, signal()), error => error.status === 400)
  const roundTrip = parseReportSearch(reportSearchParams({ ...search, view: 'agents', page: 2, status: 3 }))
  assert.equal(roundTrip.value.page, 2); assert.equal(roundTrip.value.createdFrom, filter.createdFrom)
  const catalog = createCatalogApi(coordinator.client)
  async function setMembership(active) {
    const team = await catalog.detail('teams', input.teamId, signal())
    const members = await catalog.members(input.teamId, { page: 1, pageSize: 100 }, signal())
    const member = members.items.find(row => row.userId === input.agentId); assert(member)
    await catalog.memberStatus(input.teamId, input.agentId, active, member.version, team.version, signal())
  }
  await setMembership(false)
  await parity(agent, 0) // Same permission strings; current membership changes the actual report scope.
  await setMembership(true)
  const result = { reportsVerified: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runReportingBrowser } = await import('./reporting-browser.mjs')
    await runReportingBrowser({ ...input, filter, signal, setMembership, services: { requester, agent, coordinator } })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/reporting-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend reporting journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const client of clients) { await client.session.logout().catch(() => {}); client.queryClient.clear() }
  await vite.close()
}
