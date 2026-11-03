import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'

// Only use this probe with the dedicated demo. Credentials are read locally and never printed.
const origin = new URL(process.argv[2] || 'http://127.0.0.1:8090')
assert(!origin.username && !origin.password && origin.pathname === '/' && !origin.search && !origin.hash)
assert(origin.protocol === 'https:' || (origin.protocol === 'http:' && ['127.0.0.1', 'localhost'].includes(origin.hostname)))
const base = origin.origin
const get = (path, init) => fetch(base + path, { redirect: 'manual', signal: AbortSignal.timeout(20000), ...init })
let hub
let stage = 'frontend'
try {
  const home = await get('/'); assert.equal(home.status, 200)
  const html = await home.text(); assert(html.includes('/assets/') && html.includes('.js'))
  for (const path of ['/login', '/knowledge', '/tickets/11111111-1111-1111-1111-111111111111']) {
    const response = await get(path); assert.equal(response.status, 200); assert.equal(await response.text(), html)
  }
  assert.equal((await get('/assets/not-present.js')).status, 404)
  assert.equal((await get('/.env')).status, 404)
  stage = 'API routing and ACL'
  const health = await get('/health/live'); assert.equal(health.status, 200)
  const me = await get('/api/v1/users/me'); assert.equal(me.status, 401)
  assert(!((await me.text()).includes('<html')))
  const missing = await get('/api/not-present'); assert.equal(missing.status, 404)
  assert(missing.headers.get('content-type')?.includes('application/problem+json'))
  const sources = await get('/api/v1/public/knowledge'); assert.equal(sources.status, 200)
  assert.equal((await get('/api/v1/public/knowledge', { headers: { 'X-Forwarded-Host': 'attacker.example', 'X-Forwarded-Proto': 'https' } })).status, 200)
  stage = 'WebSocket'
  hub = new HubConnectionBuilder().withUrl(base + '/hubs/community', { transport: HttpTransportType.WebSockets, skipNegotiation: true })
    .configureLogging(LogLevel.None).build()
  await hub.start(); await hub.stop()
  if (process.argv.includes('--login')) {
    stage = 'login/me/logout'
    const file = resolve(import.meta.dirname, '../../.local/self-host-demo/credentials.txt')
    const credentials = await readFile(file, 'utf8')
    const email = credentials.match(/^Email: (.+)$/m)?.[1]?.trim()
    const password = credentials.match(/^Password: (.+)$/m)?.[1]?.trim()
    assert(email && password)
    const login = await get('/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) })
    assert.equal(login.status, 200)
    const session = await login.json(); assert(session.accessToken)
    const profile = await get('/api/v1/users/me', { headers: { Authorization: 'Bearer ' + session.accessToken } })
    assert.equal(profile.status, 200)
    // Logout through the same proxy; do not leave a probe refresh session active.
    const logout = await get('/api/v1/auth/logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: session.refreshToken }) })
    assert(logout.ok)
  }
  process.stdout.write('Self-host demo verified: SPA, API routing, health, anonymous ACL, WebSocket' + (process.argv.includes('--login') ? ', login/me/logout' : '') + '.\n')
} catch {
  process.stderr.write(`Self-host demo probe failed at ${stage}. Response bodies, tokens and credentials were omitted.\n`)
  process.exitCode = 1
} finally { if (hub) await hub.stop() }
