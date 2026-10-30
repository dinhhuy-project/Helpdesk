import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'

export async function runDiscussionBrowser(input, admin, agentId) {
  const { chromium } = await import(pathToFileURL(process.env.HELPDESK_PLAYWRIGHT_MODULE).href)
  const browser = await chromium.launch({ channel: process.env.HELPDESK_BROWSER_CHANNEL || 'msedge', headless: true })
  const errors = [], ownerResponses = [], ownerRequests = []
  let closing = false
  try {
    async function open(credentials, owner = false) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
      await context.route('**/api/v1/**', async route => {
        try {
          const url = new URL(route.request().url())
          if (owner) ownerRequests.push({ url, method: route.request().method() })
          // No fixture responses: forward every request to the actual isolated application.
          const response = await route.fetch({ url: input.baseUrl + url.pathname + url.search })
          if (owner && /\/(comments|activities)$/.test(url.pathname)) ownerResponses.push(await response.text())
          await route.fulfill({ response })
        } catch {
          // Navigation/query cancellation and teardown may dispose an in-flight route.
          // Never rethrow Playwright transport errors, which include Authorization headers.
          if (!closing && !route.request().failure()) errors.push('HTTP forwarding failed')
          await route.abort().catch(() => {})
        }
      })
      const page = await context.newPage(); page.on('pageerror', () => errors.push('browser page error'))
      await page.goto(process.env.HELPDESK_FRONTEND_URL + '/login')
      await page.getByRole('textbox', { name: /Email/ }).fill(credentials.email)
      await page.getByLabel('Mật khẩu', { exact: false }).fill(credentials.password)
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
      await page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }).waitFor()
      return page
    }
    const owner = await open(input.requester, true), agent = await open(input.agent)
    await owner.locator('.desktop-sidebar').getByRole('link', { name: 'Tạo yêu cầu', exact: true }).click()
    await owner.getByRole('textbox', { name: 'Tiêu đề', exact: true }).fill(`${input.prefix}browser conversation`)
    await owner.getByRole('textbox', { name: 'Nội dung yêu cầu', exact: true }).fill('Requester creates and exchanges through the real UI.')
    await owner.getByRole('radio').first().check()
    await owner.getByRole('button', { name: 'Gửi yêu cầu', exact: true }).click()
    await owner.getByRole('heading', { name: `${input.prefix}browser conversation`, exact: true }).waitFor()
    const ticketId = new URL(owner.url()).pathname.split('/').at(-1)
    const detail = await admin.api.detail(ticketId, new AbortController().signal)
    await admin.client.post(`/api/v1/tickets/${ticketId}/assign`, { teamId: input.teamId, agentId, version: detail.version })
    await agent.locator('.desktop-sidebar').getByRole('link', { name: 'Danh sách yêu cầu', exact: true }).click()
    await agent.getByRole('link', { name: /browser conversation/ }).click()
    await agent.getByRole('button', { name: 'Ghi chú nội bộ', exact: true }).click()
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('browser-private-internal-marker')
    await agent.getByRole('button', { name: 'Gửi ghi chú nội bộ', exact: true }).click()
    await agent.getByText('browser-private-internal-marker', { exact: true }).waitFor()
    await agent.getByRole('button', { name: 'Bình luận công khai', exact: true }).click()
    await agent.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('Agent browser public response')
    await agent.getByRole('button', { name: 'Gửi bình luận', exact: true }).click()
    await agent.getByText('Agent browser public response', { exact: true }).waitFor()
    await owner.getByRole('button', { name: 'Tải lại thảo luận', exact: true }).click()
    await owner.getByText('Agent browser public response', { exact: true }).waitFor()
    assert.equal(await owner.getByText('browser-private-internal-marker', { exact: true }).count(), 0)
    assert.equal(await owner.getByRole('button', { name: 'Nội bộ', exact: true }).count(), 0)
    await owner.getByRole('textbox', { name: 'Nội dung bình luận', exact: true }).fill('Requester browser reply')
    await owner.getByRole('button', { name: 'Gửi bình luận', exact: true }).click()
    await owner.getByText('Requester browser reply', { exact: true }).waitFor()
    await agent.getByRole('button', { name: 'Tải lại thảo luận', exact: true }).click()
    await agent.getByText('Requester browser reply', { exact: true }).waitFor()
    await owner.setViewportSize({ width: 390, height: 844 })
    assert(await owner.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
    assert(!ownerResponses.join('').includes('browser-private-internal-marker'))
    assert(ownerRequests.filter(item => item.method === 'GET' && /\/(comments|activities)$/.test(item.url.pathname))
      .every(item => item.url.searchParams.get('isInternal') === 'false'))
    assert.equal(errors.length, 0)
    if (process.env.HELPDESK_SCREENSHOT_DIR) {
      await owner.bringToFront()
      await owner.getByText('Requester browser reply', { exact: true }).waitFor()
      await owner.getByRole('region', { name: 'Lịch sử hoạt động' }).getByText('Đã thêm bình luận công khai', { exact: true }).first().waitFor()
      await owner.evaluate(() => window.scrollTo(0, 0))
      await owner.screenshot({ path: join(process.env.HELPDESK_SCREENSHOT_DIR, 'discussion-requester-mobile.png'), fullPage: true })
      await agent.getByRole('button', { name: 'Nội bộ', exact: true }).click()
      await agent.getByText('browser-private-internal-marker', { exact: true }).waitFor()
      await agent.evaluate(() => window.scrollTo(0, 0))
      await agent.screenshot({ path: join(process.env.HELPDESK_SCREENSHOT_DIR, 'discussion-agent-desktop.png'), fullPage: true })
    }
    return ticketId
  } finally { closing = true; await browser.close() }
}
