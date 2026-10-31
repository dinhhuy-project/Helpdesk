import assert from 'node:assert/strict'
import { createServer } from 'vite'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
const input = JSON.parse(await new Promise(resolve => {
  let value = ''; process.stdin.setEncoding('utf8'); process.stdin.on('data', chunk => { value += chunk }); process.stdin.on('end', () => resolve(value))
}))
const vite = await createServer({ configFile: false, mode: 'test', logLevel: 'silent', server: { middlewareMode: true, hmr: false } })
let services, connection
try {
  const { createAppServices } = await vite.ssrLoadModule('/src/app/providers/app-services.ts')
  const { createCommunityApi, createPublicCommunityApi } = await vite.ssrLoadModule('/src/features/community/api/community-api.ts')
  const { createHttpClient } = await vite.ssrLoadModule('/src/shared/api/http-client.ts')
  services = createAppServices({ baseURL: input.baseUrl }); await services.session.login(input.admin)
  const api = createCommunityApi(services.client), publicApi = createPublicCommunityApi(createHttpClient({ baseURL: input.baseUrl }))
  const signal = () => new AbortController().signal
  const articles = await publicApi.list('knowledge', { page: 1, search: '' }, signal()); assert.equal(articles.totalCount, 6)
  assert.equal((await publicApi.list('knowledge', { page: 1, search: 'VPN' }, signal())).totalCount, 1)
  const draft = await api.save('knowledge', undefined, { title: 'Community journey VPN', summary: 'Public safe summary', content: 'Public safe instructions', topic: 'Network', status: null, version: 0 }, signal())
  await assert.rejects(publicApi.get('knowledge', draft.id, signal()), e => e.status === 404)
  connection = new HubConnectionBuilder().withUrl(input.baseUrl + '/hubs/community', { transport: HttpTransportType.LongPolling }).configureLogging(LogLevel.None).build()
  let resolveEvent
  const event = new Promise(resolve => { resolveEvent = resolve })
  connection.on('CommunityChanged', payload => resolveEvent(payload))
  await connection.start()
  await assert.rejects(connection.invoke('WatchTicket', draft.id))
  let row = await api.publish('knowledge', draft, '', signal())
  const changed = await Promise.race([event, new Promise((_, reject) => setTimeout(() => reject(new Error('Public invalidation timeout')), 10000))])
  assert.deepEqual(Object.keys(changed).sort(), ['id', 'kind']); assert.equal(changed.id, row.id)
  assert.equal((await publicApi.get('knowledge', row.id, signal())).revision, 1)
  row = await api.save('knowledge', row.id, { ...row, title: 'private-draft-never-public' }, signal())
  assert.equal((await publicApi.get('knowledge', row.id, signal())).title, 'Community journey VPN')
  const incident = (await publicApi.list('incidents', { page: 1, search: '', active: 'true' }, signal())).items[0]; assert(incident)
  await api.participate('incidents', incident.id, true, false, signal()); await api.participate('incidents', incident.id, true, false, signal())
  assert.equal((await publicApi.get('incidents', incident.id, signal())).affectedCount, 1)
  await api.participate('knowledge', row.id, true, true, signal())
  assert.equal((await publicApi.get('knowledge', row.id, signal())).solvedCount, 1)
  await api.withdraw('knowledge', row, signal()); await assert.rejects(publicApi.get('knowledge', row.id, signal()), e => e.status === 404)
  const result = { publicVerified: true, realtimeVerified: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runCommunityBrowser } = await import('./community-browser.mjs')
    await runCommunityBrowser({ ...input, api, publicApi, article: articles.items[0], incident })
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (error) {
  const location = error instanceof Error ? error.stack?.match(/community-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  process.stderr.write(`Community journey failed (${location ?? 'module setup'}); credentials and response bodies omitted.\n`); process.exitCode = 1
} finally {
  await connection?.stop().catch(() => {})
  await services?.session.logout().catch(() => {}); services?.queryClient.clear(); await vite.close()
}
