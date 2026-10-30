// Actual frontend TypeScript API/auth/cache against the application and PostgreSQL test fixture.
// Credentials arrive through stdin, never logs or arguments. Optional browser mode proxies real HTTP.
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
  const { ticketKeys } = await vite.ssrLoadModule('/src/features/tickets/hooks/use-tickets.ts')
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, api: createTicketApi(services.client) }
  }
  const requester = await login(input.requester), agent = await login(input.agent), admin = await login(input.admin)
  const signal = () => new AbortController().signal
  const ticket = await requester.api.create({ title: `${input.prefix}frontend exchange`, description: 'Requester creates and exchanges with support.', categoryId: input.categoryId, priority: 2 }, signal())
  assert.equal(ticket.requesterId, requester.session.store.getState().user.id)
  assert.equal(ticket.createdByUserId, ticket.requesterId)
  const agentId = agent.session.store.getState().user.id
  await admin.client.post(`/api/v1/tickets/${ticket.id}/assign`, { teamId: input.teamId, agentId, version: ticket.version })
  const ownerFlags = await requester.api.discussionAccess(ticket.id, signal())
  assert.equal(ownerFlags.canReadInternal, false); assert.equal(ownerFlags.canCommentInternal, false); assert.equal(ownerFlags.canCommentPublic, true)
  assert.equal((await agent.api.discussionAccess(ticket.id, signal())).canReadInternal, true)
  const note = await agent.api.comment(ticket.id, { content: 'private-internal-marker', isInternal: true }, signal())
  const response = await agent.api.comment(ticket.id, { content: 'Agent public response', isInternal: false }, signal())
  const reply = await requester.api.comment(ticket.id, { content: 'Requester public reply', isInternal: false }, signal())
  assert.equal(note.authorId, agentId); assert.equal(reply.authorId, ticket.requesterId)
  const scope = requester.session.store.getState().scope
  const publicSearch = { page: 1, pageSize: 20, descending: true, isInternal: false }
  const page = await requester.queryClient.fetchQuery({ queryKey: ticketKeys.comments(scope, ticket.id, publicSearch),
    queryFn: ({ signal }) => requester.api.comments(ticket.id, publicSearch, signal) })
  assert.equal(page.totalCount, 2); assert.deepEqual(new Set(page.items.map(x => x.id)), new Set([reply.id, response.id]))
  const activity = await requester.queryClient.fetchQuery({ queryKey: ticketKeys.activities(scope, ticket.id, 1),
    queryFn: ({ signal }) => requester.api.activities(ticket.id, 1, signal, false) })
  assert(!activity.items.some(x => x.action === 'InternalNoteAdded'))
  const cache = JSON.stringify(requester.queryClient.getQueryCache().getAll().map(x => x.state.data))
  assert(!cache.includes('private-internal-marker')); assert(!cache.includes(note.id))
  const internal = await agent.api.comments(ticket.id, { ...publicSearch, isInternal: true }, signal())
  assert.equal(internal.items[0].id, note.id)
  const result = { ticketId: ticket.id, requesterCachePublic: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runDiscussionBrowser } = await import('./ticket-discussion-browser.mjs')
    result.browserTicketId = await runDiscussionBrowser(input, admin, agentId)
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/ticket-discussion-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  process.stderr.write(`Frontend discussion journey failed (${location ?? 'module setup'}); credentials and response bodies are omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
