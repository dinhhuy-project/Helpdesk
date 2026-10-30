import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runSlaBrowser(input) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || undefined, headless: true })
  const errors = [], pages = []
  let closing = false
  try {
    async function open(credentials) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, timezoneId: 'America/New_York' })
      await context.route('**/api/v1/**', async route => {
        try {
          const url = new URL(route.request().url())
          await route.fulfill({ response: await route.fetch({ url: input.baseUrl + url.pathname + url.search }) })
        } catch {
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      await context.route('**/hubs/**', route => route.abort())
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', () => errors.push('browser page error'))
      page.on('console', message => {
        if (message.type() === 'error' && !message.location().url.includes('/hubs/') && !message.text().includes('status of 409')) errors.push('browser console error')
      })
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const manager = await open(input.manager), requester = await open(input.requester), agent = await open(input.agent)
    await manager.locator('.desktop-sidebar').getByRole('link', { name: 'Chính sách SLA', exact: true }).click()
    await manager.getByRole('link', { name: 'Tạo policy SLA', exact: true }).click()
    const urgentName = input.prefix + 'Browser urgent'
    await manager.getByRole('textbox', { name: 'Tên policy', exact: true }).fill(urgentName)
    await manager.getByRole('combobox', { name: 'Mức ưu tiên', exact: true }).selectOption('4')
    await manager.getByRole('spinbutton', { name: 'Phản hồi đầu tiên (phút)', exact: true }).fill('10')
    await manager.getByRole('spinbutton', { name: 'Giải quyết (phút)', exact: true }).fill('60')
    async function save(page, create = false) {
      await page.getByRole('button', { name: create ? 'Tạo policy' : 'Lưu policy', exact: true }).click()
      await page.getByRole('dialog', { name: create ? 'Xác nhận tạo policy SLA' : 'Xác nhận lưu policy SLA', exact: true })
        .getByRole('button', { name: 'Xác nhận lưu', exact: true }).click()
    }
    await save(manager, true)
    await manager.getByRole('heading', { name: urgentName, exact: true }).waitFor()
    // The manager has only ticket.manage.sla, independently of Ticket read rights.
    assert.equal(await manager.locator('.desktop-sidebar').getByRole('link', { name: 'Danh sách yêu cầu', exact: true }).count(), 0)
    await manager.getByRole('link', { name: 'Về danh sách policy', exact: true }).click()
    await manager.locator(`a[href="/admin/sla-policies/${input.policy.id}"]`).click()
    const draftName = input.prefix + 'Browser reviewed'
    await manager.getByRole('textbox', { name: 'Tên policy', exact: true }).fill(draftName)
    const current = await input.services.manager.sla.detail(input.policy.id, input.signal())
    await input.services.manager.sla.update(current.id, { ...current, resolutionMinutes: 150 }, current.version, input.signal())
    await save(manager)
    await manager.getByText(/Policy đã thay đổi. Bản nhập được giữ/).waitFor()
    assert.equal(await manager.getByRole('textbox', { name: 'Tên policy', exact: true }).inputValue(), draftName)
    await manager.getByRole('button', { name: 'Tải lại để xem xét', exact: true }).click()
    await manager.getByRole('button', { name: 'Dùng phiên bản mới để tiếp tục', exact: true }).click()
    await save(manager)
    await manager.getByRole('heading', { name: draftName, exact: true }).waitFor()
    const fresh = await input.create('Browser SLA')
    async function detail(page, ticket, title) {
      await page.setViewportSize({ width: 1440, height: 1000 })
      await page.locator('.desktop-sidebar').getByRole('link', { name: 'Danh sách yêu cầu', exact: true }).click()
      await page.getByRole('textbox', { name: 'Tìm trong tiêu đề', exact: true }).fill(input.prefix + title)
      await page.getByRole('button', { name: 'Áp dụng bộ lọc', exact: true }).click()
      await page.locator(`a[href="/tickets/${ticket.id}"]`).click()
      await page.getByRole('heading', { name: input.prefix + title, exact: true }).waitFor()
      await page.locator('.sla-panel').getByText(draftName, { exact: true }).waitFor()
    }
    await detail(requester, fresh, 'Browser SLA')
    const panel = requester.locator('.sla-panel')
    assert.equal(await panel.getByRole('link', { name: 'Xem policy hiện tại', exact: true }).count(), 0)
    await panel.getByText(/America\/New_York/).waitFor()
    const sla = (await input.services.requester.sla.ticket(fresh.id, input.signal())).sla
    const date = new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/New_York' }).format(new Date(sla.resolutionDueAt))
    await panel.getByRole('region', { name: 'Giải quyết', exact: true }).getByText(date, { exact: true }).waitFor()
    const assigned = await input.services.allocator.api.assign(fresh.id, { teamId: input.teamId, agentId: input.agentId, version: fresh.version }, input.signal())
    await input.services.agent.api.transition(fresh.id, { status: 2, version: assigned.version, resolutionSummary: null }, input.signal())
    await detail(agent, fresh, 'Browser SLA')
    await agent.getByRole('button', { name: 'Ghi chú nội bộ', exact: true }).click()
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('Internal SLA browser note.')
    await agent.getByRole('button', { name: 'Gửi ghi chú nội bộ', exact: true }).click()
    await agent.locator('.sla-panel').getByText('Chưa ghi nhận phản hồi hợp lệ', { exact: true }).waitFor()
    await agent.getByRole('button', { name: 'Bình luận công khai', exact: true }).click()
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('Public SLA browser response.')
    await agent.getByRole('button', { name: 'Gửi bình luận', exact: true }).click()
    await agent.locator('.sla-panel').getByText('Đã ghi nhận phản hồi hợp lệ', { exact: true }).waitFor()
    await panel.getByRole('button', { name: 'Tải lại SLA', exact: true }).click()
    await panel.getByText('Đã ghi nhận phản hồi hợp lệ', { exact: true }).waitFor()
    for (const [button, expected] of [['Ngừng policy', 'Đã ngừng hoạt động'], ['Kích hoạt policy', 'Đang hoạt động']]) {
      await manager.getByRole('button', { name: button, exact: true }).click()
      await manager.getByRole('dialog', { name: 'Xác nhận trạng thái policy SLA', exact: true })
        .getByRole('button', { name: 'Xác nhận trạng thái', exact: true }).click()
      await manager.getByText(expected, { exact: true }).waitFor()
    }
    await requester.setViewportSize({ width: 390, height: 844 })
    await panel.scrollIntoViewIfNeeded()
    assert(await requester.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    if (process.env.HELPDESK_BROWSER_ARTIFACTS) {
      await requester.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'sla-ticket-mobile.png'), fullPage: true })
      await manager.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, 'sla-policy-desktop.png'), fullPage: true })
    }
    assert.deepEqual(errors, [])
  } catch (failure) {
    if (process.env.HELPDESK_BROWSER_ARTIFACTS)
      for (const [index, page] of pages.entries()) await page.screenshot({ path: join(process.env.HELPDESK_BROWSER_ARTIFACTS, `sla-failure-${index}.png`), fullPage: true }).catch(() => {})
    throw failure
  } finally { closing = true; await browser.close() }
}
