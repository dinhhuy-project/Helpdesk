import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { createServer } from 'node:http'
import { request as httpsRequest } from 'node:https'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve, dirname } from 'node:path'
import { createHash, randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'

const exec = promisify(execFile), name = `helpdesk-web-test-${randomUUID()}`
const directory = await mkdtemp(join(tmpdir(), 'helpdesk-web-test-'))
const requests = []
const upstream = createServer(async (req, res) => {
  let body = ''; for await (const chunk of req) body += chunk
  requests.push({ url: req.url, method: req.method, authorization: req.headers.authorization, forwardedProto: req.headers['x-forwarded-proto'], body })
  const status = req.method === 'OPTIONS' ? 204 : req.url === '/api/v1/users/me' || req.url.startsWith('/hubs/notifications/negotiate') ? 401 : req.url.startsWith('/api/missing') ? 404 : 200
  res.writeHead(status, { 'Content-Type': 'application/json' })
  res.end(JSON.stringify({ ok: true }))
})
upstream.on('upgrade', (req, socket) => {
  requests.push({ url: req.url, upgrade: req.headers.upgrade })
  const accept = createHash('sha1').update(req.headers['sec-websocket-key'] + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64')
  socket.end(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`)
})
await new Promise(resolve => upstream.listen(0, '0.0.0.0', resolve))
let started = false
try {
  const openssl = process.platform === 'win32' ? (process.env.HELPDESK_OPENSSL || 'C:/Program Files/Git/usr/bin/openssl.exe') : 'openssl'
  await exec(openssl, ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=localhost',
    '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1', '-keyout', join(directory, 'privkey.pem'), '-out', join(directory, 'fullchain.pem')])
  await exec('docker', ['run', '-d', '--rm', '--name', name, '--add-host', 'host.docker.internal:host-gateway',
    '-p', '127.0.0.1::8080', '-p', '127.0.0.1::8443', '-e', `API_UPSTREAM=http://host.docker.internal:${upstream.address().port}`,
    '--mount', `type=bind,source=${directory},target=/etc/nginx/tls,readonly`, process.argv[2] || 'helpdesk-web:acceptance'])
  started = true
  const port = async value => Number((await exec('docker', ['port', name, value])).stdout.trim().split(':').at(-1))
  const httpPort = await port('8080/tcp'), httpsPort = await port('8443/tcp')
  const ca = await readFile(join(directory, 'fullchain.pem'))
  const get = (path, options = {}) => new Promise((resolve, reject) => {
    const request = httpsRequest({ host: '127.0.0.1', servername: 'localhost', port: httpsPort, path, ca, timeout: 10_000, ...options }, response => {
      let body = ''; response.setEncoding('utf8'); response.on('data', chunk => { body += chunk })
      response.on('end', () => resolve({ status: response.statusCode, headers: response.headers, body }))
    })
    request.on('upgrade', (response, socket) => { socket.destroy(); resolve({ status: response.statusCode, headers: response.headers }) })
    request.on('timeout', () => request.destroy(new Error('Web image probe timed out')))
    request.on('error', reject); request.end(options.body)
  })
  let home
  for (let i = 0; i < 40; i++) {
    try { home = await get('/'); if (home.status === 200) break } catch { /* Wait for Nginx, not an external service. */ }
    await new Promise(resolve => setTimeout(resolve, 250))
  }
  assert.equal(home?.status, 200)
  assert.equal(home.headers['cache-control'], 'no-store')
  for (const path of ['/login', '/tickets/11111111-1111-1111-1111-111111111111', '/reports/tickets?status=3', '/admin/users']) {
    const response = await get(path); assert.equal(response.status, 200); assert.equal(response.body, home.body)
  }
  const asset = home.body.match(/src="(\/assets\/[^" ]+\.js)"/)?.[1]; assert(asset)
  const js = await get(asset); assert.equal(js.status, 200); assert.match(js.headers['cache-control'], /immutable/)
  assert.equal((await get('/assets/missing.js')).status, 404)
  assert.equal((await get('/.env')).status, 404)
  const redirect = await fetch(`http://127.0.0.1:${httpPort}/tickets?status=3`, { redirect: 'manual' })
  assert.equal(redirect.status, 308); assert.equal(redirect.headers.get('location'), 'https://127.0.0.1/tickets?status=3')
  await get('/api/v1/auth/login', { method: 'POST', headers: { Authorization: 'Bearer probe-only', 'Content-Type': 'application/json' }, body: '{"probe":true}' })
  assert.equal((await get('/api/missing')).status, 404)
  assert.equal((await get('/health/live')).headers['content-type'], 'application/json')
  const hub = '/hubs/notifications?access_token=probe-query-not-a-real-token'
  const socket = await get(hub, { headers: { Upgrade: 'websocket', Connection: 'Upgrade', 'Sec-WebSocket-Key': 'dGhlIHNhbXBsZSBub25jZQ==', 'Sec-WebSocket-Version': '13' } })
  assert.equal(socket.status, 101)
  assert.equal(socket.headers['sec-websocket-accept'], 's3pPLMBiTxaQ9kYGzzhZRbK+xOo=')
  assert(requests.some(item => item.url === '/api/v1/auth/login' && item.method === 'POST' && item.authorization === 'Bearer probe-only' && item.forwardedProto === 'https' && item.body === '{"probe":true}'))
  assert(requests.some(item => item.url === hub && item.upgrade === 'websocket'))
  const logs = (await exec('docker', ['logs', name])).stdout
  assert(!logs.includes('probe-query-not-a-real-token')); assert(!logs.includes('Bearer probe-only'))
  await exec(process.execPath, [join(dirname(fileURLToPath(import.meta.url)), 'verify-deployment.mjs'), `https://localhost:${httpsPort}`], {
    env: { ...process.env, NODE_EXTRA_CA_CERTS: join(directory, 'fullchain.pem') },
  })
  process.stdout.write('Web image verified: trusted TLS, redirect, SPA deep links, asset caching, API/body/Bearer forwarding, WSS upgrade and safe access logs.\n')
} catch (failure) {
  const location = failure?.stack?.match(/verify-web-image\.mjs:\d+:\d+/)?.[0] || 'setup'
  process.stderr.write(`Web image acceptance failed at ${location}; credentials, certificate keys and request bodies omitted.\n`); process.exitCode = 1
} finally {
  if (started) await exec('docker', ['stop', name]).catch(() => {})
  await new Promise(resolve => upstream.close(resolve))
  // Only remove the unique directory this process created, under the OS temporary directory.
  if (dirname(resolve(directory)) === resolve(tmpdir()) && directory.split(/[\\/]/).at(-1).startsWith('helpdesk-web-test-')) await rm(directory, { recursive: true, force: true })
}
