import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runNotificationBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || 'msedge', headless: true })
  const errors = [], pages = []
  let closing = false
  try {
    let ticket = await input.create('Browser inbox')
    ticket = await input.services.coordinator.api.assign(ticket.id, { teamId: input.teamId, agentId: input.agentId, version: ticket.version }, input.signal())
    await input.services.coordinator.api.comment(ticket.id, { content: 'Public browser notification body.', isInternal: false }, input.signal())
    await input.services.coordinator.api.comment(ticket.id, { content: 'Internal browser notification secret.', isInternal: true }, input.signal())
    async function open(credentials) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
      // REST inbox must work while no SignalR transport is available.
      await context.route('**/hubs/**', route => route.abort())
      await context.route('**/api/v1/**', async route => {
        try {
          const url = new URL(route.request().url())
          await route.fulfill({ response: await route.fetch({ url: input.baseUrl + url.pathname + url.search }) })
        } catch {
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', () => errors.push('browser page error'))
      page.on('console', message => {
        if (message.type() === 'error' && !message.text().includes('status of 404') && !message.location().url.includes('/hubs/')) errors.push('browser console error')
      })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const requester = await open(input.requester), agent = await open(input.agent)
    const inbox = await input.services.requester.inbox.list({ page: 1, pageSize: 100 }, input.signal())
    const selected = inbox.items.find(item => item.ticketId === ticket.id && item.kind === 'PublicCommentAdded')
    assert(selected)
    const before = (await input.services.requester.inbox.count(input.signal())).count
    await requester.getByRole('link', { name: `Thông báo: ${before} chưa đọc`, exact: true }).waitFor()
    await requester.locator('.notification-bell').focus(); await requester.keyboard.press('Enter')
    await requester.getByRole('heading', { name: 'Thông báo', exact: true }).waitFor()
    await requester.getByRole('combobox', { name: 'Trạng thái đọc', exact: true }).selectOption('true')
    assert(requester.url().includes('unread=true'))
    assert.equal(await requester.getByText('Nội bộ', { exact: true }).count(), 0)
    assert.equal(await requester.getByText('Internal browser notification secret.', { exact: true }).count(), 0)
    await requester.locator(`a[href="/notifications/${selected.id}"]`).click()
    await requester.getByRole('link', { name: 'Mở yêu cầu liên quan', exact: true }).waitFor()
    assert.equal((await input.services.requester.inbox.detail(selected.id, input.signal())).readAt, null)
    await requester.getByRole('button', { name: 'Đánh dấu đã đọc', exact: true }).click()
    await requester.getByText('Đã đánh dấu thông báo là đã đọc.', { exact: true }).waitFor()
    await requester.getByRole('link', { name: `Thông báo: ${before - 1} chưa đọc`, exact: true }).waitFor()
    await requester.getByRole('link', { name: 'Về danh sách thông báo', exact: true }).click()
    await requester.locator('.notification-list').waitFor()
    assert.equal(await requester.locator(`a[href="/notifications/${selected.id}"]`).count(), 0)
    await requester.getByRole('combobox', { name: 'Trạng thái đọc', exact: true }).selectOption('false')
    await requester.locator(`a[href="/notifications/${selected.id}"]`).click()
    await requester.getByRole('link', { name: 'Mở yêu cầu liên quan', exact: true }).click()
    await requester.getByRole('heading', { name: input.prefix + 'Browser inbox', exact: true }).waitFor()
    const teamInbox = await input.services.agent.inbox.list({ page: 1, pageSize: 100 }, input.signal())
    const internal = teamInbox.items.find(item => item.ticketId === ticket.id && item.isInternal)
    assert(internal)
    await agent.locator('.notification-bell').click()
    await agent.locator(`a[href="/notifications/${internal.id}"]`).click()
    await agent.getByRole('link', { name: 'Mở yêu cầu liên quan', exact: true }).waitFor()
    await agent.getByRole('heading', { name: internal.title, exact: true }).waitFor()
    await agent.getByText('Nội bộ', { exact: true }).waitFor()
    await input.setMembership(false)
    await agent.getByRole('button', { name: 'Đánh dấu đã đọc', exact: true }).click()
    await agent.getByRole('heading', { name: 'Thông báo không còn khả dụng', exact: true }).waitFor()
    await agent.getByRole('link', { name: 'Thông báo: 0 chưa đọc', exact: true }).waitFor()
    assert.equal(await agent.getByRole('heading', { name: internal.title, exact: true }).count(), 0)
    // Profile permission strings are unchanged by team membership; the REST resource ACL still denies access.
    assert.equal(await agent.getByRole('link', { name: 'Mở yêu cầu liên quan', exact: true }).count(), 0)
    await requester.locator('.notification-bell').click()
    await requester.getByRole('heading', { name: 'Thông báo', exact: true }).waitFor()
    await requester.setViewportSize({ width: 390, height: 844 })
    assert(await requester.locator('.notification-bell').isVisible())
    assert(await requester.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'notifications-inbox-mobile.png'), fullPage: true })
      await agent.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'notifications-access-lost.png'), fullPage: true })
    }
    await input.setMembership(true)
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS)
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `notifications-failure-${index}.png`), fullPage: true }).catch(() => {})
    throw failure
  } finally { closing = true; await browser.close() }
}
