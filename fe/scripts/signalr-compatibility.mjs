// Invoked by the isolated .NET integration test. Fixture credentials arrive on stdin, never argv/logs.
import { HubConnectionBuilder, HttpTransportType, LogLevel, VERSION } from '@microsoft/signalr'

let step = 'read fixture'
let connection
let refreshToken
let baseUrl
let changed = 0
try {
  let input = ''
  for await (const chunk of process.stdin) input += chunk
  const fixture = JSON.parse(input)
  baseUrl = fixture.baseUrl
  const require = (condition, label) => { if (!condition) throw new Error(label) }
  const request = (path, options = {}) => fetch(new URL(path, baseUrl), options)
  const post = (path, body) => request(path, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  })
  step = 'anonymous negotiate'
  require((await post('/hubs/notifications/negotiate?negotiateVersion=1', {})).status === 401, step)
  step = 'login and me'
  const login = await post('/api/v1/auth/login', { email: fixture.email, password: fixture.password })
  require(login.status === 200, step)
  const pair = await login.json()
  let accessToken = pair.accessToken
  refreshToken = pair.refreshToken
  const me = () => request('/api/v1/users/me', { headers: { Authorization: `Bearer ${accessToken}` } })
  require((await me()).status === 200, step)
  step = 'JS10 connect and initial event'
  require(VERSION.startsWith('10.'), step)
  connection = new HubConnectionBuilder().withUrl(new URL('/hubs/notifications', baseUrl).href, {
    transport: HttpTransportType.LongPolling, withCredentials: false, accessTokenFactory: () => accessToken,
  }).configureLogging(LogLevel.None).build()
  connection.on('NotificationsChanged', () => { changed++ })
  await connection.start()
  await connection.invoke('Ping')
  require(changed >= 1, step)
  step = 'ticket group authorization'
  await connection.invoke('WatchTicket', fixture.ticketId)
  let denied = false
  try { await connection.invoke('WatchTicket', fixture.outsideTicketId) } catch { denied = true }
  require(denied, step)
  await connection.invoke('UnwatchTicket', fixture.ticketId)
  step = 'query token rejected by REST'
  require((await request(`/api/v1/users/me?access_token=${encodeURIComponent(accessToken)}`)).status === 401, step)
  step = 'rotate and restart with fresh JWT'
  await connection.stop()
  const oldAccessToken = accessToken
  const rotated = await post('/api/v1/auth/refresh', { refreshToken })
  require(rotated.status === 200, step)
  const next = await rotated.json()
  accessToken = next.accessToken
  refreshToken = next.refreshToken
  require((await request('/api/v1/users/me', { headers: { Authorization: `Bearer ${oldAccessToken}` } })).status === 401, step)
  require((await me()).status === 200, step)
  await connection.start()
  await connection.invoke('Ping')
  await connection.invoke('WatchTicket', fixture.ticketId)
  require(changed >= 2, step)
  await connection.stop()
  step = 'logout and revoked JWT'
  require((await post('/api/v1/auth/logout', { refreshToken })).status === 204, step)
  refreshToken = undefined
  require((await me()).status === 401, step)
  let revoked = false
  try { await connection.start() } catch { revoked = true }
  require(revoked, step)
  console.log(JSON.stringify({ clientVersion: VERSION, transport: 'LongPolling', changedEvents: changed,
    groupDenied: denied, rotation: true, revoked: true }))
} catch {
  // Never print SDK exceptions or request data: browser-compatible errors may contain access_token URLs.
  console.error(`SignalR compatibility failed at: ${step}`)
  process.exitCode = 1
} finally {
  try { await connection?.stop() } catch { /* Fixture cleanup below still runs. */ }
  if (refreshToken && baseUrl) {
    try { await fetch(new URL('/api/v1/auth/logout', baseUrl), { method: 'POST',
      headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }) }) } catch { /* Test database is disposable. */ }
  }
}
