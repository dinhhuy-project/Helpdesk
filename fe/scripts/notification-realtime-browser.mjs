import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runRealtimeBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || 'msedge', headless: true })
  const pages = [], errors = [], requesterRequests = []
  let closing = false
  try {
    let ticket = await input.create('Browser realtime')
    ticket = await input.services.coordinator.api.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, input.signal())
    async function open(credentials, capture = false) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
      for (const pattern of ['**/api/v1/**', '**/hubs/**']) await context.route(pattern, async route => {
        try {
          const url = new URL(route.request().url())
          if (capture) requesterRequests.push({ path: url.pathname, internal: url.searchParams.get('isInternal') })
          const response = await route.fetch({ url: input.baseUrl + url.pathname + url.search, timeout: 0 })
          // The TestServer bridge forwards HTTP, not WebSockets. Exercise real LongPolling in the browser.
          if (url.pathname.endsWith('/negotiate') && response.ok()) {
            const negotiation = await response.json()
            negotiation.availableTransports = negotiation.availableTransports.filter(item => item.transport === 'LongPolling')
            await route.fulfill({ response, json: negotiation })
          } else await route.fulfill({ response })
        } catch {
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', () => errors.push('browser page error'))
      page.on('console', message => { if (message.type() === 'error' && !message.text().includes('status of 404')) errors.push('browser console error') })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const requester = await open(input.requester, true), secondRequester = await open(input.requester)
    const agent = await open(input.agent)
    async function until(check) {
      for (let i = 0; i < 80; i++) { if (await check()) return; await new Promise(resolve => setTimeout(resolve, 100)) }
      assert.fail('Browser realtime state did not converge')
    }
    async function detail(page) {
      await page.locator('.desktop-sidebar').getByRole('link', { name: 'Danh sách yêu cầu', exact: true }).click()
      await page.getByRole('textbox', { name: 'Tìm trong tiêu đề', exact: true }).fill(input.prefix + 'Browser realtime')
      await page.getByRole('button', { name: 'Áp dụng bộ lọc', exact: true }).click()
      await page.locator(`a[href="/tickets/${ticket.id}"]`).click()
      await page.getByRole('heading', { name: input.prefix + 'Browser realtime', exact: true }).waitFor()
      await page.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).waitFor()
    }
    await detail(requester); await detail(agent)
    await until(async () => (await input.registry(input.services.requester)).some(row => row.tickets.includes(ticket.id)))
    const draft = 'Bản nháp của requester được giữ khi có cập nhật trực tiếp.'
    await requester.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill(draft)
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('Public reply over live SignalR.')
    await agent.getByRole('button', { name: 'Gửi bình luận', exact: true }).click()
    await agent.locator('.comment-item').getByText('Public reply over live SignalR.', { exact: true }).waitFor()
    await input.control(input.services.coordinator, 'dispatch')
    await requester.locator('.comment-item').getByText('Public reply over live SignalR.', { exact: true }).waitFor()
    assert.equal(await requester.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).inputValue(), draft)
    await agent.getByRole('button', { name: 'Ghi chú nội bộ', exact: true }).click()
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('Private browser realtime note.')
    await agent.getByRole('button', { name: 'Gửi ghi chú nội bộ', exact: true }).click()
    await agent.locator('.comment-item').getByText('Private browser realtime note.', { exact: true }).waitFor()
    await input.control(input.services.coordinator, 'dispatch')
    assert.equal(await requester.getByText('Private browser realtime note.', { exact: true }).count(), 0)
    assert(!requesterRequests.some(request => request.path.endsWith('/comments') && request.internal === 'true'))
    await requester.locator('.notification-bell').click()
    await requester.getByText('Đang nhận cập nhật trực tiếp.', { exact: true }).waitFor()
    await until(async () => (await input.registry(input.services.requester)).every(row => !row.tickets.includes(ticket.id)))
    const all = await input.services.requester.inbox.list({ page: 1, pageSize: 100, unread: true }, input.signal())
    const selected = all.items.find(item => item.ticketId === ticket.id && item.kind === 'PublicCommentAdded')
    assert(selected)
    const before = (await input.services.requester.inbox.count(input.signal())).count
    await secondRequester.getByRole('link', { name: `Thông báo: ${before} chưa đọc`, exact: true }).waitFor()
    const row = requester.locator('.notification-item').filter({ has: requester.locator(`a[href="/notifications/${selected.id}"]`) })
    await row.getByRole('button', { name: /Đánh dấu đã đọc/ }).click()
    await input.control(input.services.coordinator, 'dispatch')
    await secondRequester.getByRole('link', { name: `Thông báo: ${before - 1} chưa đọc`, exact: true }).waitFor()
    const oldIds = (await input.registry(input.services.requester)).map(row => row.id)
    await input.control(input.services.requester, 'disconnect')
    await until(async () => { const rows = await input.registry(input.services.requester); return rows.length === 2 && rows.every(row => !oldIds.includes(row.id)) })
    await requester.getByText('Đang nhận cập nhật trực tiếp.', { exact: true }).waitFor()
    await input.setMembership(false); await input.control(input.services.coordinator, 'connections')
    await agent.evaluate(() => dispatchEvent(new Event('focus')))
    await agent.getByRole('heading', { name: 'Không tìm thấy yêu cầu', exact: true }).waitFor()
    assert.equal(await agent.getByText('Private browser realtime note.', { exact: true }).count(), 0)
    assert((await input.registry(input.services.agent)).every(row => !row.tickets.includes(ticket.id)))
    await requester.setViewportSize({ width: 390, height: 844 })
    assert(await requester.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'realtime-inbox-mobile.png'), fullPage: true })
      await agent.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'realtime-scope-lost.png'), fullPage: true })
    }
    await requester.getByRole('button', { name: 'Đăng xuất', exact: true }).click()
    await requester.getByRole('heading', { name: 'Đăng nhập', exact: true }).waitFor()
    await until(async () => (await input.registry(input.services.requester)).length === 1)
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS)
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `realtime-failure-${index}.png`), fullPage: true }).catch(() => {})
    throw failure
  } finally { closing = true; await browser.close() }
}
