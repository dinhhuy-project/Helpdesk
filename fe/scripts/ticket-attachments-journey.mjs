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
  const { createTicketAttachmentApi } = await vite.ssrLoadModule('/src/features/tickets/api/ticket-attachment-api.ts')
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services); await services.session.login(credentials)
    return { ...services, tickets: createTicketApi(services.client), files: createTicketAttachmentApi(services.client) }
  }
  const requester = await login(input.requester), agent = await login(input.agent), outsider = await login(input.outsider), coordinator = await login(input.coordinator)
  const signal = () => new AbortController().signal
  const create = title => requester.tickets.create({ title: input.prefix + title, description: 'Private MinIO attachment journey.', categoryId: input.categoryId, priority: 2 }, signal())
  const ticket = await create('Attachments client')
  const target = { commentId: null, isInternal: false }
  const options = await requester.files.options(ticket.id, null, signal())
  assert.equal(options.access.canReadInternal, false); assert.equal(options.access.canUploadPublic, true)
  assert(options.policy.maxFileSizeBytes > 0)
  for (const hidden of ['bucket', 'endpoint', 'accessKey', 'secretKey', 'objectKey']) assert(!JSON.stringify(options).includes(hidden))
  await coordinator.tickets.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, signal())
  const body = 'Private UTF8 file: Tiếng Việt.'
  const file = new File([body], 'evidence.txt', { type: 'text/plain' })
  const saved = await requester.files.upload(ticket.id, target, file, file.type, signal(), () => {})
  assert.equal(saved.uploadedByUserId, input.requesterId)
  const metadata = await requester.files.metadata(ticket.id, saved.id, target, signal())
  const blob = await requester.files.download(ticket.id, metadata, signal())
  assert.equal(await blob.text(), body)
  const comment = await requester.tickets.comment(ticket.id, { content: 'Public comment attachment.', isInternal: false }, signal())
  const commentTarget = { commentId: comment.id, isInternal: false }
  assert.equal((await requester.files.options(ticket.id, comment.id, signal())).access.canUploadPublic, true)
  assert.equal((await agent.files.options(ticket.id, comment.id, signal())).access.canUploadPublic, false)
  const commentFile = await requester.files.upload(ticket.id, commentTarget, new File(['comment'], 'comment.txt', { type: 'text/plain' }), 'text/plain', signal(), () => {})
  assert.equal(commentFile.commentId, comment.id)
  await assert.rejects(agent.files.upload(ticket.id, commentTarget, file, file.type, signal(), () => {}), e => e.status === 403)
  const note = await agent.tickets.comment(ticket.id, { content: 'Internal comment attachment.', isInternal: true }, signal())
  const internalTarget = { commentId: note.id, isInternal: true }
  const internal = await agent.files.upload(ticket.id, internalTarget, new File(['internal'], 'private-agent.txt', { type: 'text/plain' }), 'text/plain', signal(), () => {})
  assert.equal(internal.isInternal, true)
  const search = { page: 1, pageSize: 10, descending: true, ...target }
  const publicFiles = await requester.files.list(ticket.id, search, signal())
  assert.equal(publicFiles.totalCount, 2); assert(publicFiles.items.every(item => !item.isInternal))
  assert(!JSON.stringify(publicFiles).includes('private-agent.txt'))
  await assert.rejects(requester.files.options(ticket.id, note.id, signal()), e => e.status === 404)
  await assert.rejects(requester.files.metadata(ticket.id, internal.id, internalTarget, signal()), e => e.status === 404)
  // Binary 404 is normalized into ProblemDetails instead of being saved as a file.
  await assert.rejects(requester.files.download(ticket.id, internal, signal()), e => e.status === 404 && !!e.traceId)
  await assert.rejects(outsider.files.options(ticket.id, null, signal()), e => e.status === 404)
  await assert.rejects(outsider.files.download(ticket.id, saved, signal()), e => e.status === 404)
  const scoped = await requester.files.list(ticket.id, { ...search, commentId: comment.id }, signal())
  assert.equal(scoped.totalCount, 1); assert.equal(scoped.items[0].id, commentFile.id)
  const result = { filesVerified: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runAttachmentBrowser } = await import('./ticket-attachments-browser.mjs')
    await runAttachmentBrowser({ ...input, create, services: { requester, agent, coordinator }, signal })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/ticket-attachments-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend attachments journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
