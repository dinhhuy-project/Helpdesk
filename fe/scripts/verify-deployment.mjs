import assert from 'node:assert/strict'

// Read-only smoke probe. Does not login, seed, migrate, create Tickets or modify external services.
const results = []
try {
  if (process.env.NODE_TLS_REJECT_UNAUTHORIZED === '0') throw new Error('TLS verification must remain enabled.')
  const origin = new URL(process.argv[2]), api = new URL(process.argv[3] || origin)
  for (const url of [origin, api]) assert(url.protocol === 'https:' && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash)
  const request = async (name, url, options = {}) => {
    const response = await fetch(url, { ...options, redirect: 'manual', signal: AbortSignal.timeout(15_000) })
    results.push({ name, status: response.status }); return response
  }
  const home = await request('SPA document over trusted HTTPS', origin)
  assert.equal(home.status, 200); assert.match(home.headers.get('content-type') || '', /text\/html/)
  const html = await home.text()
  for (const path of ['/login', '/tickets/11111111-1111-1111-1111-111111111111', '/reports/tickets', '/admin/users']) {
    const response = await request(`SPA deep link ${path}`, new URL(path, origin))
    assert.equal(response.status, 200); assert.equal(await response.text(), html)
  }
  const asset = html.match(/src="(\/assets\/[^" ]+\.js)"/)?.[1]; assert(asset)
  const js = await request('Built JavaScript', new URL(asset, origin)); assert.equal(js.status, 200)
  assert.match(js.headers.get('content-type') || '', /(?:javascript|ecmascript)/)
  assert.equal((await request('Missing asset remains 404', new URL('/assets/missing.js', origin))).status, 404)
  const live = await request('Backend liveness', new URL('/health/live', api))
  assert.equal(live.status, 200); assert(!/text\/html/.test(live.headers.get('content-type') || ''))
  const user = await request('API authentication challenge', new URL('/api/v1/users/me', api))
  const hub = await request('Hub authentication challenge', new URL('/hubs/notifications/negotiate?negotiateVersion=1', api), { method: 'POST' })
  for (const response of [user, hub]) { assert.equal(response.status, 401); assert(!/text\/html/.test(response.headers.get('content-type') || '')) }
  const preflight = async from => request('CORS preflight', new URL('/api/v1/users/me', api), { method: 'OPTIONS', headers: {
    Origin: from, 'Access-Control-Request-Method': 'GET', 'Access-Control-Request-Headers': 'authorization,content-type',
  } })
  if (origin.origin !== api.origin) {
    const allowed = await preflight(origin.origin)
    assert.equal(allowed.headers.get('access-control-allow-origin'), origin.origin)
    assert.notEqual(allowed.headers.get('access-control-allow-credentials'), 'true')
  }
  assert.equal((await preflight('https://untrusted.invalid')).headers.get('access-control-allow-origin'), null)
  process.stdout.write(JSON.stringify({ passed: true, checks: results }, null, 2) + '\n')
} catch {
  process.stderr.write(JSON.stringify({ passed: false, checks: results, message: 'Deployment probe failed. Check HTTPS/origins, SPA fallback, API/hub routing and CORS; response bodies omitted.' }, null, 2) + '\n')
  process.exitCode = 1
}
