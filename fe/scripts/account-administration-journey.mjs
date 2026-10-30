// Real frontend TypeScript clients against disposable application/PostgreSQL. No fixture HTTP responses.
import assert from 'node:assert/strict'
import { randomBytes } from 'node:crypto'
import { createServer } from 'vite'
const input = JSON.parse(await new Promise(resolve => {
  let value = ''; process.stdin.setEncoding('utf8'); process.stdin.on('data', chunk => { value += chunk }); process.stdin.on('end', () => resolve(value))
}))
const vite = await createServer({ configFile: false, mode: 'test', logLevel: 'silent', server: { middlewareMode: true, hmr: false } })
const clients = []
try {
  const { createAppServices } = await vite.ssrLoadModule('/src/app/providers/app-services.ts')
  const { createAccountApi } = await vite.ssrLoadModule('/src/features/accounts/api/account-api.ts')
  async function login(credentials) {
    const services = createAppServices({ baseURL: input.baseUrl }); clients.push(services)
    await services.session.login(credentials)
    return { ...services, api: createAccountApi(services.client) }
  }
  const admin = await login(input.admin), manager = await login(input.manager), roleManager = await login(input.roleManager)
  const search = { page: 1, pageSize: 20, sortBy: 'createdAt', descending: true }
  const password = randomBytes(24).toString('hex'), email = `${input.prefix}-client@example.test`
  let user = await manager.api.create({ email, displayName: 'Frontend account journey', password })
  assert.deepEqual(user.roles.map(role => role.name), ['Requester'])
  assert.equal((await manager.api.list({ ...search, search: email }, false)).items[0].id, user.id)
  const before = user.version
  const departments = await manager.api.departments(1, input.departmentName)
  assert(departments.items.some(x => x.id === input.departmentId))
  user = await manager.api.update(user.id, { displayName: 'Updated account', departmentId: input.departmentId, version: user.version })
  assert.equal(user.departmentId, input.departmentId)
  assert.equal(user.departmentName, input.departmentName)
  await assert.rejects(manager.api.update(user.id, { displayName: 'Stale', departmentId: null, version: before }), e => e.code === 'VersionConflict')
  const adminUserId = admin.session.store.getState().user.id
  assert.equal((await manager.api.access(adminUserId)).canUpdateProfile, false)
  await assert.rejects(manager.api.setRoles(user.id, [], user.version), e => e.status === 403)
  await assert.rejects(roleManager.api.list(search, false), e => e.status === 403)
  assert((await roleManager.api.list({ ...search, search: email }, true)).items.some(x => x.id === user.id))
  const access = await roleManager.api.roleAccess()
  assert.equal(access.canManageGrants, false); assert.equal(access.canReadAudit, false)
  assert(access.assignableRoleIds.includes(input.ordinaryRoleId))
  user = await roleManager.api.setRoles(user.id, [input.ordinaryRoleId], user.version)
  await assert.rejects(roleManager.api.detail(adminUserId, true), e => e.status === 404)
  const permissions = await admin.api.permissions()
  let role = (await admin.api.roles()).find(x => x.id === input.ordinaryRoleId)
  role = await admin.api.grants(role.id, [permissions.find(x => x.code === 'ticket.create').id], role.version)
  assert.deepEqual(role.permissions, ['ticket.create'])
  const target = await login({ email, password })
  const changed = randomBytes(24).toString('hex')
  target.queryClient.setQueryData(['private-account-marker'], { id: user.id })
  await target.session.changePassword({ currentPassword: password, newPassword: changed })
  assert.equal(target.session.store.getState().reason, 'passwordChanged')
  assert.equal(target.queryClient.getQueryCache().getAll().length, 0)
  await assert.rejects(target.session.login({ email, password }), e => e.status === 401)
  await target.session.login({ email, password: changed })
  user = await manager.api.detail(user.id, false)
  user = await manager.api.status(user.id, false, user.version)
  await assert.rejects(target.session.refreshProfile(), e => e.status === 401)
  assert.equal(target.session.store.getState().status, 'anonymous')
  user = await manager.api.status(user.id, true, user.version)
  await target.session.login({ email, password: changed })
  assert.equal(target.session.store.getState().status, 'authenticated')
  const audit = await admin.api.audit(1)
  assert(audit.items.some(x => x.action === 'UserStatusChanged' && x.targetUserId === user.id))
  const result = { revokedSessionsHandled: true }
  if (process.env.HELPDESK_PLAYWRIGHT_MODULE && process.env.HELPDESK_FRONTEND_URL) {
    const { runAccountBrowser } = await import('./account-administration-browser.mjs')
    await runAccountBrowser(input)
    result.browserVerified = true
  }
  process.stdout.write(JSON.stringify(result))
} catch (failure) {
  const location = failure instanceof Error ? failure.stack?.match(/account-administration-(?:journey|browser)\.mjs:\d+:\d+/)?.[0] : undefined
  const status = Number.isInteger(failure?.status) ? failure.status : 'none'
  const kind = ['http', 'network', 'timeout', 'cancelled', 'unexpected'].includes(failure?.kind) ? failure.kind : 'assertion/setup'
  process.stderr.write(`Frontend account journey failed (${location ?? 'module setup'}, status=${status}, kind=${kind}); credentials and response bodies omitted.\n`)
  process.exitCode = 1
} finally {
  for (const service of clients) { await service.session.logout().catch(() => {}); service.queryClient.clear() }
  await vite.close()
}
