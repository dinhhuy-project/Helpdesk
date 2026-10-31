import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { mkdir } from 'node:fs/promises'
import { preview } from 'vite'

const frontend = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const root = resolve(frontend, '..')
const configuration = process.argv.includes('--release') ? 'Release' : 'Debug'
const artifacts = join(frontend, 'test-results', 'acceptance')
await mkdir(artifacts, { recursive: true })
async function run(command, args, cwd, env = process.env) {
  const child = spawn(command, args, { cwd, env, stdio: 'inherit', windowsHide: true })
  const stop = () => child.kill()
  process.once('SIGINT', stop); process.once('SIGTERM', stop)
  try {
    await new Promise((resolve, reject) => {
      child.on('error', reject)
      child.on('exit', code => code === 0 ? resolve() : reject(new Error(`Acceptance subprocess failed: ${command}`)))
    })
  } finally { process.off('SIGINT', stop); process.off('SIGTERM', stop) }
}
let server
try {
  for (const name of ['HELPDESK_TEST_CONNECTION', 'HELPDESK_TEST_MINIO_ENDPOINT', 'HELPDESK_TEST_MINIO_ACCESS_KEY', 'HELPDESK_TEST_MINIO_SECRET_KEY']) {
    if (!process.env[name]?.trim()) throw new Error('Isolated database/storage test configuration is required.')
  }
  // Test the deployable bundle, never the development/HMR server.
  const npmCli = process.env.npm_execpath || (process.platform === 'win32' ? join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js') : undefined)
  if (npmCli) await run(process.execPath, [npmCli, 'run', 'build'], frontend)
  else await run('npm', ['run', 'build'], frontend)
  server = await preview({ root: frontend, server: { proxy: {} }, preview: { host: 'localhost', port: 0, strictPort: true, proxy: {} } })
  const address = server.httpServer.address()
  if (!address || typeof address === 'string') throw new Error('Cannot resolve acceptance server port.')
  const journeys = ['Discussion', 'Account', 'Catalog', 'Workflow', 'Attachment', 'Sla', 'Notification', 'Realtime', 'Reporting', 'Community']
  const selected = process.env.HELPDESK_ACCEPTANCE_JOURNEYS?.split(',') || journeys
  if (!selected.length || selected.some(name => !journeys.includes(name))) throw new Error('Invalid acceptance journey selection.')
  const filters = selected.map(name => `FullyQualifiedName~Frontend${name}JourneyTests`).join('|')
  await run('dotnet', ['test', join(root, 'be/be.sln'), '--configuration', configuration, '--no-restore',
    '--filter', filters, '--logger', 'trx;LogFileName=browser-acceptance.trx', '--results-directory', artifacts], root, {
    ...process.env, HELPDESK_PLAYWRIGHT_MODULE: join(frontend, 'node_modules/playwright/index.mjs'),
    HELPDESK_FRONTEND_URL: `http://localhost:${address.port}`, HELPDESK_BROWSER_ARTIFACTS: artifacts,
    HELPDESK_RELEASE_ACCEPTANCE: 'true',
  })
} catch {
  process.stderr.write('Browser acceptance failed; see sanitized test results.\n'); process.exitCode = 1
} finally {
  if (server) await new Promise(resolve => server.httpServer.close(resolve))
}
